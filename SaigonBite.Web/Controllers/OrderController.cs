using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;
using SaigonBite.Web.Models;
using SaigonBite.Web.Services;

namespace SaigonBite.Web.Controllers;

/// <summary>
/// FR-C1..C5: customer ordering UI. The controller only calls OrderService and
/// OrderStatusService; every business rule stays in the service/Core layers.
/// </summary>
[Authorize(Policy = "Customer")]
public sealed class OrderController : Controller
{
    private const string CartKey = "SaigonBite.Cart";

    private readonly OrderService _orders;
    private readonly OrderStatusService _status;

    public OrderController(OrderService orders, OrderStatusService status)
    {
        _orders = orders;
        _status = status;
    }

    [HttpGet]
    public async Task<IActionResult> Hubs() =>
        View(new HubsViewModel { Hubs = await _orders.GetHubCardsAsync() });

    [HttpGet]
    public async Task<IActionResult> Menu(int hubId)
    {
        var hub = (await _orders.GetHubCardsAsync()).FirstOrDefault(h => h.Id == hubId);
        if (hub is null) return NotFound();
        return View(new MenuViewModel
        {
            HubId = hub.Id,
            HubName = hub.Name,
            Brands = await _orders.GetMenuAsync(hub.Id)
        });
    }

    // FR-C2: only positive quantities become cart lines; BR12 is still checked by the service.
    [HttpPost]
    public IActionResult Cart(int hubId, int[] itemIds, int[] quantities)
    {
        var ids = itemIds ?? Array.Empty<int>();
        var qs = quantities ?? Array.Empty<int>();
        var cart = new List<CartInput>();
        for (var i = 0; i < Math.Min(ids.Length, qs.Length); i++)
            if (qs[i] > 0)
                cart.Add(new CartInput { MenuItemId = ids[i], Quantity = qs[i] });

        TempData[CartKey] = JsonSerializer.Serialize(cart);
        return RedirectToAction(nameof(Checkout), new { hubId });
    }

    [HttpGet]
    public async Task<IActionResult> Checkout(int hubId) =>
        View(await BuildCheckoutAsync(hubId, new CheckoutViewModel()));

    // FR-C3: one voucher box; Apply only re-prices, it never writes.
    [HttpPost]
    public async Task<IActionResult> Preview(CheckoutViewModel model) =>
        View("Checkout", await BuildCheckoutAsync(model.HubId, model));

    [HttpPost]
    public async Task<IActionResult> Place(CheckoutViewModel model)
    {
        var outcome = await _orders.PlaceOrderAsync(ToPlaceInput(model));
        if (!outcome.Accepted)
        {
            var rejected = await BuildCheckoutAsync(model.HubId, model);
            rejected.Error = outcome.Message ?? "The order was rejected.";
            rejected.RejectReason = outcome.RuleReason;
            rejected.LineErrors = outcome.LineErrors;
            return View("Checkout", rejected);
        }

        TempData["NoticeKey"] = "OrderPlaced";
        return RedirectToAction(nameof(Track), new { id = outcome.OrderId });
    }

    [HttpGet]
    public async Task<IActionResult> Track(int id)
    {
        var detail = await _orders.GetDetailAsync(id);
        if (detail.CustomerId != CurrentUserId()) return Forbid();
        return View(await BuildTrackAsync(detail));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _status.CancelAsync(id, CurrentUserId(), reason);
        if (!result.Accepted)
        {
            var vm = await BuildTrackAsync(await _orders.GetDetailAsync(id));
            vm.Error = result.Error;
            return View("Track", vm);
        }

        TempData["NoticeKey"] = "OrderCancelled";
        return RedirectToAction(nameof(Track), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var detail = await _orders.GetDetailAsync(id);
        if (detail.CustomerId != CurrentUserId()) return Forbid();
        if (!_status.CustomerActions(detail.Status).CanEdit)
        {
            TempData["NoticeKey"] = "EditOnlyWhilePlaced";
            return RedirectToAction(nameof(Track), new { id });
        }

        return View(new EditItemsViewModel
        {
            OrderId = id,
            Lines = detail.Lines.Select(l => new CartLine
            {
                MenuItemId = l.MenuItemId,
                Name = l.ItemName,
                Category = l.Category,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditItemsViewModel model)
    {
        var lines = model.Lines.Select(l => new OrderLineInput(l.MenuItemId, l.Quantity)).ToList();
        var result = await _status.EditItemsAsync(model.OrderId, CurrentUserId(), lines);
        if (!result.Accepted)
        {
            model.Error = result.Error;
            return View(model);
        }

        TempData["NoticeKey"] = "ItemsUpdated";
        return RedirectToAction(nameof(Track), new { id = model.OrderId });
    }

    [HttpGet]
    public async Task<IActionResult> MyOrders() =>
        View(new MyOrdersViewModel { Orders = await _orders.GetCustomerOrdersAsync(CurrentUserId()) });

    private async Task<CheckoutViewModel> BuildCheckoutAsync(int hubId, CheckoutViewModel input)
    {
        input.HubId = hubId;
        input.Districts = _orders.Districts;
        input.IsTourist = CurrentRole() == Role.Tourist;

        var cart = input.Lines.Count > 0
            ? input.Lines.Select(l => new CartInput { MenuItemId = l.MenuItemId, Quantity = l.Quantity }).ToList()
            : ReadCart();
        var items = await _orders.GetItemsAsync(cart.Select(c => c.MenuItemId));
        input.Lines = cart.Where(c => items.ContainsKey(c.MenuItemId)).Select(c =>
        {
            var item = items[c.MenuItemId];
            return new CartLine
            {
                MenuItemId = item.Id, Quantity = c.Quantity, Name = item.Name,
                Category = item.Category, UnitPrice = item.Price
            };
        }).ToList();

        // Preview and placement share OrderService.PrepareAsync, so this card is the final price (BR17).
        var preview = await _orders.PreviewPriceAsync(ToPlaceInput(input));
        if (preview.Accepted)
        {
            input.Breakdown = preview.Breakdown;
            input.Notice = preview.Breakdown?.VoucherNote;
        }
        else
        {
            input.Error = preview.Message;
            input.RejectReason = preview.Reason;
            input.LineErrors = preview.LineErrors;
        }

        var total = input.Breakdown?.Total
                    ?? input.Lines.Sum(l => l.UnitPrice * l.Quantity);
        input.PaymentMethods = _orders.AllowedPaymentMethods(CurrentRole(), total);
        if (input.PaymentMethods.Count > 0 && !input.PaymentMethods.Contains(input.PaymentMethod))
            input.PaymentMethod = input.PaymentMethods[0];
        return input;
    }

    private async Task<TrackViewModel> BuildTrackAsync(OrderDetailDto detail)
    {
        return new TrackViewModel
        {
            Detail = detail,
            History = await _orders.GetHistoryAsync(detail.Id),
            Actions = _status.CustomerActions(detail.Status),
            Notice = TempData["NoticeKey"] as string
        };
    }

    private PlaceOrderInput ToPlaceInput(CheckoutViewModel m) => new(
        CurrentUserId(), m.HubId, m.District,
        m.Lines.Where(l => l.Quantity > 0)
            .Select(l => new OrderLineInput(l.MenuItemId, l.Quantity)).ToList(),
        m.PaymentMethod,
        string.IsNullOrWhiteSpace(m.VoucherCode) ? null : new[] { m.VoucherCode },
        m.DeliveryAddress, m.HotelName, m.RoomNumber);

    private List<CartInput> ReadCart()
    {
        var raw = TempData[CartKey] as string;
        return string.IsNullOrEmpty(raw)
            ? new List<CartInput>()
            : JsonSerializer.Deserialize<List<CartInput>>(raw) ?? new List<CartInput>();
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    private Role CurrentRole() =>
        Enum.TryParse<Role>(User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty, out var role)
            ? role
            : Role.Local;
}

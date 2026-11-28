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
    private readonly CheckoutDistanceService _distance;

    public OrderController(OrderService orders, OrderStatusService status, CheckoutDistanceService distance)
    {
        _orders = orders;
        _status = status;
        _distance = distance;
    }

    [HttpGet]
    public async Task<IActionResult> Hubs() =>
        View(new HubsViewModel { Hubs = await _orders.GetHubCardsAsync() });

    /// <summary>
    /// Live hub load for the hub page (polled every 10 s). Customers only (class-level policy);
    /// returns just what the meters need: no names, no order data.
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> HubLoads()
    {
        var hubs = await _orders.GetHubCardsAsync();
        return Json(hubs.Select(h => new
        {
            id = h.Id,
            busy = h.Busy,
            capacity = h.Capacity,
            load = h.Load,
            isBusy = h.IsBusy,
            isQuiet = h.IsQuiet
        }));
    }

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

    // A-06: entering Checkout draws the distance once and seals it in a ticket (no client ticket here).
    [HttpGet]
    public async Task<IActionResult> Checkout(int hubId)
    {
        var model = new CheckoutViewModel();
        var quote = _distance.Resolve(CurrentUserId(), hubId, model.District, null);
        return View(await BuildCheckoutAsync(hubId, model, quote));
    }

    // FR-C3: one voucher box; Apply only re-prices, it never writes. The ticket keeps the km unchanged.
    [HttpPost]
    public async Task<IActionResult> Preview(CheckoutViewModel model)
    {
        var quote = _distance.Resolve(CurrentUserId(), model.HubId, model.District, model.DistanceTicket);
        return View("Checkout", await BuildCheckoutAsync(model.HubId, model, quote));
    }

    [HttpPost]
    public async Task<IActionResult> Place(CheckoutViewModel model)
    {
        var quote = _distance.Resolve(CurrentUserId(), model.HubId, model.District, model.DistanceTicket);
        if (!quote.IsReused)
        {
            // Missing, forged, expired or other-hub ticket: never place with an unverified distance.
            // Show the freshly priced checkout (new km, new ticket) and let the customer confirm again.
            var refreshed = await BuildCheckoutAsync(model.HubId, model, quote);
            refreshed.Error = "DistanceRecalculated"; // Checkout.cshtml maps this key to the localised text
            return View("Checkout", refreshed);
        }

        var outcome = await _orders.PlaceOrderAsync(ToPlaceInput(model, quote.Km));
        if (!string.IsNullOrWhiteSpace(outcome.RedirectUrl))
            return Redirect(outcome.RedirectUrl);

        if (!outcome.Accepted)
        {
            var rejected = await BuildCheckoutAsync(model.HubId, model, quote);
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

    private async Task<CheckoutViewModel> BuildCheckoutAsync(int hubId, CheckoutViewModel input, DistanceQuote quote)
    {
        input.HubId = hubId;
        // Whatever the client posted for km/ticket is replaced by the server's verified quote.
        ModelState.Remove(nameof(CheckoutViewModel.DistanceTicket));
        ModelState.Remove(nameof(CheckoutViewModel.DistanceKm));
        input.DistanceKm = quote.Km;
        input.DistanceTicket = quote.Ticket;
        input.Districts = _orders.Districts;
        var customerId = CurrentUserId();
        var customerRole = await _orders.GetCustomerRoleAsync(customerId);
        input.IsTourist = customerRole == Role.Tourist;

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
        var preview = await _orders.PreviewPriceAsync(ToPlaceInput(input, quote.Km));
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

        // BR13/BR14: filter by DB role and the rounded total from Preview (never subtotal-only or cookie role).
        if (input.Breakdown is { Total: var roundedTotal })
        {
            input.PaymentMethods = await _orders.AllowedPaymentMethodsForCustomerAsync(customerId, roundedTotal);
            if (input.PaymentMethods.Count > 0 && !input.PaymentMethods.Contains(input.PaymentMethod))
                input.PaymentMethod = input.PaymentMethods[0];
        }
        else
        {
            input.PaymentMethods = Array.Empty<PaymentMethod>();
        }

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

    private PlaceOrderInput ToPlaceInput(CheckoutViewModel m, decimal distanceKm) => new(
        CurrentUserId(), m.HubId, m.District ?? string.Empty,
        m.Lines.Where(l => l.Quantity > 0)
            .Select(l => new OrderLineInput(l.MenuItemId, l.Quantity)).ToList(),
        m.PaymentMethod,
        string.IsNullOrWhiteSpace(m.VoucherCode) ? null : new[] { m.VoucherCode },
        m.DeliveryAddress ?? string.Empty, m.HotelName ?? string.Empty, m.RoomNumber ?? string.Empty,
        distanceKm);

    private List<CartInput> ReadCart()
    {
        var raw = TempData[CartKey] as string;
        return string.IsNullOrEmpty(raw)
            ? new List<CartInput>()
            : JsonSerializer.Deserialize<List<CartInput>>(raw) ?? new List<CartInput>();
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
}

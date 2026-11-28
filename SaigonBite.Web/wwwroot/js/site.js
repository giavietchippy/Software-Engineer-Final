// SaigonBite UI behaviour. Everything here is progressive: without JS the forms still post normally.
(function () {
  'use strict';

  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---------- Toasts ----------
  function toast(message, type, timeout) {
    var region = document.getElementById('sb-toasts');
    if (!region || !message) return;
    var el = document.createElement('div');
    el.className = 'sb-toast' + (type === 'error' ? ' is-error' : '');
    el.setAttribute('role', type === 'error' ? 'alert' : 'status');
    var p = document.createElement('p');
    p.textContent = message;
    var close = document.createElement('button');
    close.type = 'button';
    close.setAttribute('aria-label', region.dataset.closeLabel || 'Close');
    close.textContent = '\u00d7';
    el.appendChild(p);
    el.appendChild(close);
    region.appendChild(el);
    function dismiss() {
      el.classList.add('is-leaving');
      setTimeout(function () { el.remove(); }, reduced ? 0 : 200);
    }
    close.addEventListener('click', dismiss);
    setTimeout(dismiss, timeout || 4500);
  }
  window.sbToast = toast;

  document.querySelectorAll('[data-toast-message]').forEach(function (el) {
    toast(el.dataset.toastMessage, el.dataset.toastType || 'success');
  });

  // ---------- Confirm before destructive actions, plus busy state on submit ----------
  var confirmModalEl = document.getElementById('sb-confirm');
  var confirmModal = null;
  var pendingConfirm = null;

  function askConfirm(message, onYes) {
    if (!confirmModalEl || !window.bootstrap) {
      if (window.confirm(message)) onYes();
      return;
    }
    confirmModal = confirmModal || new window.bootstrap.Modal(confirmModalEl);
    confirmModalEl.querySelector('[data-confirm-text]').textContent = message;
    pendingConfirm = onYes;
    confirmModal.show();
  }
  if (confirmModalEl) {
    confirmModalEl.querySelector('[data-confirm-yes]').addEventListener('click', function () {
      var run = pendingConfirm;
      pendingConfirm = null;
      confirmModal.hide();
      if (run) run();
    });
  }

  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!(form instanceof HTMLFormElement)) return;

    if (form.dataset.confirm && form.dataset.confirmed !== '1') {
      e.preventDefault();
      var submitter = e.submitter;
      askConfirm(form.dataset.confirm, function () {
        form.dataset.confirmed = '1';
        if (form.requestSubmit) form.requestSubmit(submitter || undefined); else form.submit();
      });
      return;
    }

    if (form.dataset.submitting === '1') { e.preventDefault(); return; }
    form.dataset.submitting = '1';
    var btn = e.submitter || form.querySelector('[type="submit"]');
    // Added on the next tick so the clicked button's name/formaction are already read.
    setTimeout(function () { if (btn) btn.classList.add('is-busy'); }, 0);
  });

  window.addEventListener('pageshow', function (e) {
    if (!e.persisted) return;
    document.querySelectorAll('form').forEach(function (f) { delete f.dataset.submitting; delete f.dataset.confirmed; });
    document.querySelectorAll('.is-busy').forEach(function (b) { b.classList.remove('is-busy'); });
  });

  // ---------- Quantity steppers and the menu summary bar ----------
  var fmt = new Intl.NumberFormat(document.documentElement.lang || 'en');

  function bump(el) {
    if (reduced || !el) return;
    el.classList.remove('bump');
    void el.offsetWidth;
    el.classList.add('bump');
  }

  function updateSummary() {
    var bar = document.getElementById('sb-summary');
    if (!bar) return;
    var count = 0, total = 0;
    document.querySelectorAll('[data-item-row]').forEach(function (row) {
      var input = row.querySelector('input[name="quantities"]');
      var qty = parseInt(input.value, 10) || 0;
      row.classList.toggle('is-picked', qty > 0);
      count += qty;
      total += qty * (parseFloat(input.dataset.price) || 0);
    });
    var countEl = bar.querySelector('[data-sum-count]');
    var totalEl = bar.querySelector('[data-sum-total]');
    var changed = countEl.textContent !== String(count);
    if (window.sbCountTo) {
      window.sbCountTo(countEl, count, function (v) { return String(Math.round(v)); });
      window.sbCountTo(totalEl, total, function (v) { return fmt.format(Math.round(v)) + '\u00a0\u20ab'; });
    } else {
      countEl.textContent = count;
      totalEl.textContent = fmt.format(total) + '\u00a0\u20ab';
    }
    if (changed) { bump(countEl); bump(totalEl); }
    var go = bar.querySelector('[data-sum-go]');
    go.classList.toggle('is-disabled', count === 0);
    go.setAttribute('aria-disabled', count === 0 ? 'true' : 'false');
  }

  function flyPlusOne(btn) {
    if (reduced) return;
    var bar = document.getElementById('sb-summary');
    if (!bar) return;
    var a = btn.getBoundingClientRect(), b = bar.getBoundingClientRect();
    var dot = document.createElement('span');
    dot.className = 'sb-fly';
    dot.textContent = '+1';
    dot.style.left = (a.left + a.width / 2) + 'px';
    dot.style.top = (a.top + a.height / 2) + 'px';
    dot.style.setProperty('--dx', (b.left + 40 - a.left - a.width / 2) + 'px');
    dot.style.setProperty('--dy', (b.top + b.height / 2 - a.top - a.height / 2) + 'px');
    document.body.appendChild(dot);
    setTimeout(function () { dot.remove(); }, 650);
  }

  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-step]');
    if (!btn) return;
    var stepper = btn.closest('.sb-stepper');
    // Only the number box; the row also holds a hidden itemIds input that must never be touched.
    var input = stepper && stepper.querySelector('input[type="number"]');
    if (!input) return;
    var min = parseInt(input.min, 10); if (isNaN(min)) min = 0;
    var max = parseInt(input.max, 10); if (isNaN(max)) max = 99;
    var delta = parseInt(btn.dataset.step, 10);
    var before = parseInt(input.value, 10) || 0;
    var next = Math.max(min, Math.min(max, before + delta));
    input.value = next;
    if (next > before) flyPlusOne(btn);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  document.addEventListener('input', function (e) {
    if (e.target.matches('input[name="quantities"]')) updateSummary();
  });
  document.addEventListener('click', function (e) {
    var go = e.target.closest('[data-sum-go]');
    if (go && go.classList.contains('is-disabled')) e.preventDefault();
  });
  updateSummary();

  // ---------- Photos and logos: look for a file by convention, else keep the drawn placeholder ----------
  function slug(t) {
    return (t || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/\u0111/g, 'd')
      .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  }
  document.querySelectorAll('[data-img-base]').forEach(function (box) {
    var isLogo = box.classList.contains('sb-paylogo');
    var exts = isLogo ? ['svg', 'png', 'webp', 'jpg', 'jpeg'] : ['jpg', 'jpeg', 'png', 'webp'];
    var bases = [box.dataset.imgBase];
    if (box.dataset.imgName) {
      // Same folder, named after the dish (for example pho-bo.jpg) as an alternative to the Id.
      bases.push(box.dataset.imgBase.replace(/[^\/]+$/, '') + slug(box.dataset.imgName));
    }
    var candidates = [];
    bases.forEach(function (base) { exts.forEach(function (x) { candidates.push(base + '.' + x); }); });
    var i = 0;
    function tryNext() {
      if (i >= candidates.length) return;
      var img = new Image();
      img.alt = '';
      // No loading=lazy here: this image is not in the page yet, so a lazy image would never start loading.
      img.onload = function () {
        box.querySelectorAll('svg').forEach(function (n) { n.remove(); });
        box.appendChild(img);
        box.classList.add('has-img');
        var host = box.closest('.sb-banner, .sb-cover');
        if (host) host.classList.add('has-photo');
      };
      img.onerror = tryNext;
      img.src = candidates[i++];
    }
    tryNext();
  });

  // ---------- Menu: category chips, search and sort ----------
  var menuRows = Array.prototype.slice.call(document.querySelectorAll('[data-item-row]'));
  if (menuRows.length) {
    var chips = document.querySelectorAll('.sb-chip[data-filter]');
    var searchBox = document.getElementById('sb-search');
    var sortBox = document.getElementById('sb-sort');
    var noMatch = document.getElementById('sb-nomatch');
    var activeCat = 'all';
    menuRows.forEach(function (r, i) { r.dataset.order = i; });

    function plain(t) {
      return (t || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/\u0111/g, 'd');
    }
    function flip(fn) { if (window.sbFlip) window.sbFlip(fn); else fn(); }
    function applyFilters() {
      var q = plain(searchBox ? searchBox.value.trim() : '');
      var shown = 0;
      menuRows.forEach(function (row) {
        var ok = (activeCat === 'all' || row.dataset.cat === activeCat) && (!q || plain(row.dataset.name).indexOf(q) !== -1);
        row.hidden = !ok;
        if (ok) shown++;
      });
      document.querySelectorAll('[data-brand-block]').forEach(function (block) {
        block.hidden = !block.querySelector('[data-item-row]:not([hidden])');
      });
      if (noMatch) noMatch.hidden = shown !== 0;
    }
    function applySort() {
      var mode = sortBox ? sortBox.value : 'default';
      document.querySelectorAll('[data-brand-block]').forEach(function (block) {
        var list = Array.prototype.slice.call(block.querySelectorAll('[data-item-row]'));
        list.sort(function (a, b) {
          if (mode === 'default') return a.dataset.order - b.dataset.order;
          var d = (parseFloat(a.dataset.price) || 0) - (parseFloat(b.dataset.price) || 0);
          return mode === 'asc' ? d : -d;
        });
        // Rows (with their hidden itemIds) move together, so form order stays consistent.
        list.forEach(function (r) { block.appendChild(r); });
      });
    }
    chips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        activeCat = chip.dataset.filter;
        chips.forEach(function (c) {
          var on = c === chip;
          c.classList.toggle('is-on', on);
          c.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        flip(applyFilters);
      });
    });
    if (searchBox) {
      searchBox.addEventListener('input', function () { flip(applyFilters); });
      // Enter in the search box must not submit the order form.
      searchBox.addEventListener('keydown', function (e) { if (e.key === 'Enter') e.preventDefault(); });
    }
    if (sortBox) sortBox.addEventListener('change', function () { flip(applySort); });

    var clear = document.querySelector('[data-sum-clear]');
    if (clear) clear.addEventListener('click', function () {
      menuRows.forEach(function (row) { row.querySelector('input[name="quantities"]').value = 0; });
      updateSummary();
    });
  }

  // ---------- Back to top ----------
  var topBtn = document.getElementById('sb-top');
  if (topBtn) {
    window.addEventListener('scroll', function () { topBtn.hidden = window.scrollY < 400; }, { passive: true });
    topBtn.addEventListener('click', function () { window.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' }); });
  }

  // ---------- Checkout: warn when shown prices are older than the form ----------
  var stale = document.getElementById('sb-stale');
  var checkout = document.getElementById('checkout-form');
  if (stale && checkout) {
    checkout.addEventListener('input', function (e) {
      if (e.target.name !== 'PaymentMethod') stale.classList.add('show');
    });
    checkout.addEventListener('change', function (e) {
      if (e.target.name !== 'PaymentMethod') stale.classList.add('show');
    });
  }

  // ---------- Show a field group only for one value of a select ----------
  document.querySelectorAll('[data-show-when]').forEach(function (group) {
    var parts = group.dataset.showWhen.split('=');
    var select = document.querySelector(parts[0]);
    if (!select) return;
    function apply() {
      var on = select.value === parts[1];
      group.hidden = !on;
      // Hidden fields still post (as empty strings), exactly like before; just clear stale input.
      if (!on) group.querySelectorAll('input').forEach(function (f) { f.value = ''; });
    }
    select.addEventListener('change', apply);
    apply();
  });

  // Bottom tab bar: slide the indicator from the previous tab (remembered for this tab session only).
  (function () {
    var bar = document.querySelector('.sb-tabbar');
    if (!bar) return;
    try {
      var cur = parseInt(getComputedStyle(bar).getPropertyValue('--i'), 10);
      var prev = sessionStorage.getItem('sb.tab');
      if (prev !== null && !isNaN(parseInt(prev, 10))) bar.style.setProperty('--prev', parseInt(prev, 10));
      if (!isNaN(cur)) sessionStorage.setItem('sb.tab', String(cur));
    } catch (e) { /* storage unavailable: indicator simply appears in place */ }
  })();

  // ---------- Layout helpers (app-style pages) ----------
  // Sticky elements sit under the header, so publish its real height as a CSS variable.
  (function () {
    var header = document.querySelector('.sb-header');
    if (!header) return;
    function setH() { document.documentElement.style.setProperty('--sb-header-h', header.offsetHeight + 'px'); }
    setH();
    window.addEventListener('resize', setH);
  })();

  // The district chosen at checkout is remembered in this browser only, so Home can show it.
  // Nothing is shown until a real choice exists.
  (function () {
    var KEY = 'sb.district';
    var select = document.querySelector('[data-remember-district]');
    if (select) {
      var save = function () {
        try {
          var opt = select.options[select.selectedIndex];
          if (opt) localStorage.setItem(KEY, opt.textContent.trim());
        } catch (e) { /* storage unavailable: ignore */ }
      };
      select.addEventListener('change', save);
      var form = select.closest('form');
      if (form) form.addEventListener('submit', save);
    }
    var chip = document.querySelector('[data-deliver-to]');
    if (chip) {
      try {
        var saved = localStorage.getItem(KEY);
        if (saved) {
          chip.querySelector('[data-deliver-to-text]').textContent = saved;
          chip.hidden = false;
        }
      } catch (e) { /* ignore */ }
    }
  })();

  // Menu header: show the kitchen load from the same JSON endpoint the hub list uses.
  (function () {
    var box = document.querySelector('[data-menu-load]');
    if (!box || !window.fetch) return;
    var id = parseInt(box.dataset.menuLoad, 10);
    var timer = null;
    function paint(list) {
      var h = list.filter(function (x) { return x.id === id; })[0];
      if (!h) return;
      var meter = box.querySelector('[data-meter]');
      if (meter) {
        meter.style.setProperty('--s', Math.max(0, Math.min(1, h.load)));
        meter.classList.toggle('is-busy', !!h.isBusy);
        meter.classList.toggle('is-quiet', !h.isBusy && !!h.isQuiet);
      }
      var cnt = box.querySelector('[data-hub-count]');
      if (cnt) cnt.textContent = h.busy + ' / ' + h.capacity;
      box.classList.toggle('is-busy', !!h.isBusy);
      box.hidden = false;
    }
    function poll() {
      if (document.hidden) return;
      fetch(box.dataset.loadsUrl, { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } })
        .then(function (r) {
          var type = r.headers.get('content-type') || '';
          if (r.redirected || !r.ok || type.indexOf('json') === -1) { clearInterval(timer); return null; }
          return r.json();
        })
        .then(function (list) { if (list) paint(list); })
        .catch(function () { /* try again on the next tick */ });
    }
    poll();
    timer = setInterval(poll, 10000);
  })();

  // My orders: the whole card opens the order (the "View" link stays for keyboard and no-JS use).
  document.addEventListener('click', function (e) {
    var row = e.target.closest('tr[data-href]');
    if (!row || e.target.closest('a, button, input, select, label')) return;
    window.location.href = row.dataset.href;
  });

  // ---------- Client-side search and paging for tables that already hold all rows ----------
  document.querySelectorAll('table[data-paginate]').forEach(function (table) {
    var size = parseInt(table.dataset.paginate, 10) || 10;
    var rows = Array.prototype.slice.call(table.tBodies[0].rows).filter(function (r) { return !r.hasAttribute('data-empty'); });
    var filterInput = table.dataset.filter ? document.querySelector(table.dataset.filter) : null;
    var page = 0;
    var wrap = table.closest('.sb-table-wrap') || table.parentElement;
    var pager = document.createElement('div');
    pager.className = 'sb-pager';
    var info = document.createElement('span');
    var nav = document.createElement('span');
    var prev = document.createElement('button');
    var next = document.createElement('button');
    [prev, next].forEach(function (b) { b.type = 'button'; b.className = 'btn btn-outline-secondary btn-sm'; });
    prev.textContent = '\u2039'; next.textContent = '\u203a';
    prev.setAttribute('aria-label', table.dataset.prevLabel || 'Previous');
    next.setAttribute('aria-label', table.dataset.nextLabel || 'Next');
    nav.className = 'd-flex gap-1';
    nav.appendChild(prev); nav.appendChild(next);
    pager.appendChild(info); pager.appendChild(nav);
    wrap.appendChild(pager);

    function render() {
      var q = filterInput ? filterInput.value.trim().toLowerCase() : '';
      var visible = rows.filter(function (r) { return !q || r.textContent.toLowerCase().indexOf(q) !== -1; });
      var pages = Math.max(1, Math.ceil(visible.length / size));
      page = Math.min(page, pages - 1);
      rows.forEach(function (r) { r.hidden = true; });
      visible.slice(page * size, page * size + size).forEach(function (r) { r.hidden = false; });
      var from = visible.length ? page * size + 1 : 0;
      var to = Math.min(visible.length, page * size + size);
      info.textContent = from + '\u2013' + to + ' / ' + visible.length;
      prev.disabled = page === 0;
      next.disabled = page >= pages - 1;
      pager.hidden = rows.length <= size && !q;
    }
    prev.addEventListener('click', function () { page--; render(); });
    next.addEventListener('click', function () { page++; render(); });
    if (filterInput) filterInput.addEventListener('input', function () { page = 0; render(); });
    render();
  });
})();

// =====================================================================
// MOTION LAYER (Part A). Enhancement only: if anything here is missing the pages still work.
// Pure JS, no libraries. Respects prefers-reduced-motion and pauses when the tab is hidden.
// =====================================================================
(function () {
  'use strict';
  var root = document.documentElement;
  var reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  var lang = root.lang || 'en';
  var nf = new Intl.NumberFormat(lang);
  var raf = window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function (f) { return setTimeout(f, 16); };

  function each(list, fn) { Array.prototype.forEach.call(list, fn); }
  function ease(t) { return 1 - Math.pow(1 - t, 3); }
  function safe(fn) { try { fn(); } catch (e) { /* motion must never break a page */ } }

  // ----- tween a number into an element's text (used for counters, totals, summary bar) -----
  window.sbCountTo = function (el, to, format) {
    if (!el) return;
    format = format || function (v) { return String(Math.round(v)); };
    if (reduced) { el.textContent = format(to); el.dataset.val = to; return; }
    var from = parseFloat(el.dataset.val);
    if (isNaN(from)) from = parseFloat((el.textContent || '').replace(/[^\d.-]/g, '')) || 0;
    el.dataset.val = to;
    var id = el.__twId = (el.__twId || 0) + 1;
    var t0 = performance.now(), ms = 380;
    (function step(now) {
      if (el.__twId !== id) return; // a newer tween took over
      var p = Math.min(1, (now - t0) / ms);
      el.textContent = format(from + (to - from) * ease(p));
      if (p < 1) raf(step);
    })(t0);
  };

  // ----- [data-count]: count up when scrolled into view (checkout rows, distance, ...) -----
  function countFormat(el) {
    var dec = parseInt(el.dataset.decimals, 10) || 0;
    var pre = el.dataset.prefix || '', suf = el.dataset.suffix || '';
    return function (v) { return pre + (dec ? v.toFixed(dec) : nf.format(Math.round(v))) + suf; };
  }
  var countIdx = 0;
  function initCount(el) {
    if (el.__counted) return; el.__counted = true;
    var to = parseFloat(el.dataset.count);
    if (isNaN(to)) return;
    var key = 'sb.count:' + location.pathname + ':' + (countIdx++), from = 0;
    safe(function () { var p = parseFloat(sessionStorage.getItem(key)); if (!isNaN(p)) from = p; });
    safe(function () { sessionStorage.setItem(key, String(to)); });
    if (reduced || (from === to && from !== 0)) return;
    var fmt = countFormat(el);
    el.textContent = fmt(from);
    el.dataset.val = from;
    var run = function () { window.sbCountTo(el, to, fmt); };
    if (!('IntersectionObserver' in window)) { run(); return; }
    var io = new IntersectionObserver(function (es) {
      if (es.some(function (e) { return e.isIntersecting; })) { io.disconnect(); setTimeout(run, 150); }
    }, { threshold: .3 });
    io.observe(el);
  }

  // ----- first number inside a text (home rule cards, dashboard stats): "10 km" counts 0 -> 10 -----
  function countInText(el) {
    if (reduced || el.__textCounted) return; el.__textCounted = true;
    var text = el.textContent, m = /(\d{1,3}(?:[.,\u00a0 ]\d{3})+|\d+)/.exec(text);
    if (!m) return;
    var sepMatch = /[.,\u00a0 ]/.exec(m[1]), sep = sepMatch ? sepMatch[0] : '';
    var n = parseInt(m[1].replace(/[.,\u00a0 ]/g, ''), 10);
    if (isNaN(n) || n === 0) return;
    var before = text.slice(0, m.index), after = text.slice(m.index + m[1].length);
    function fmt(v) { var s = String(Math.round(v)); return sep ? s.replace(/\B(?=(\d{3})+(?!\d))/g, sep) : s; }
    var t0 = performance.now(), ms = 900;
    el.textContent = before + fmt(0) + after;
    function go() {
      t0 = performance.now();
      (function step(now) {
        var p = Math.min(1, (now - t0) / ms);
        el.textContent = before + fmt(n * ease(p)) + after;
        if (p < 1) raf(step);
      })(t0);
    }
    if (!('IntersectionObserver' in window)) { el.textContent = text; return; }
    var io = new IntersectionObserver(function (es) {
      if (es.some(function (e) { return e.isIntersecting; })) { io.disconnect(); go(); }
    }, { threshold: .4 });
    io.observe(el);
  }

  // ----- scroll reveal with stagger -----
  var REVEAL_SEL = '.sb-page-head, .card, .sb-stat, .sb-rule:not(.sb-rise), .sb-brandblock:not(.sb-rise), .sb-item, ' +
    '.sb-empty, .sb-hubrow, .alert:not([hidden]), .table > tbody > tr:not([data-empty]), .sb-steps, .sb-status-art';
  var revealIO = null;
  function armReveal() {
    if (reduced || !('IntersectionObserver' in window)) return;
    revealIO = new IntersectionObserver(function (entries) {
      var order = 0;
      entries.forEach(function (en) {
        if (!en.isIntersecting) return;
        var el = en.target; revealIO.unobserve(el);
        el.style.setProperty('--rv', Math.min(order++, 10));
        raf(function () { el.classList.add('is-in'); });
        var done = function () { el.removeAttribute('data-reveal'); el.classList.remove('is-in'); el.style.removeProperty('--rv'); };
        el.addEventListener('transitionend', function te(e) { if (e.propertyName === 'opacity') { el.removeEventListener('transitionend', te); done(); } });
        setTimeout(done, 1600); // safety net if transitionend never fires
      });
    }, { threshold: .08, rootMargin: '0px 0px -4% 0px' });
    each(document.querySelectorAll(REVEAL_SEL), function (el) {
      if (el.closest('.modal, .sb-toast, .dropdown-menu, .sb-summarybar, [data-no-reveal]')) return;
      var rowLike = el.matches('tr, .sb-item, .sb-hubrow');
      if (!rowLike && el.parentElement && el.parentElement.closest('[data-reveal]')) return; // no nested reveals
      if (el.hidden) return;
      el.setAttribute('data-reveal', '');
      revealIO.observe(el);
    });
  }

  // ----- meters: run from 0 to the real value -----
  function armMeter(m) {
    if (m.classList.contains('is-armed')) return;
    if (!m.style.getPropertyValue('--s')) {
      var w = parseFloat(m.getAttribute('data-w') || getComputedStyle(m).getPropertyValue('--w')) || 0;
      m.style.setProperty('--s', Math.max(0, Math.min(1, w / 100)));
    }
    raf(function () { raf(function () { m.classList.add('is-armed'); }); });
  }

  // ----- FLIP: filter/sort re-arrangement without jumping -----
  window.sbFlip = function (change) {
    var rows = document.querySelectorAll('[data-item-row]');
    if (reduced || !rows.length || !rows[0].animate) { change(); return; }
    var first = new Map();
    each(rows, function (r) { if (!r.hidden) first.set(r, r.getBoundingClientRect()); });
    change();
    each(rows, function (r) {
      if (r.hidden) return;
      var a = first.get(r), b = r.getBoundingClientRect();
      if (!a) { r.animate([{ opacity: 0, transform: 'scale(.96)' }, { opacity: 1, transform: 'none' }], { duration: 260, easing: 'ease-out' }); return; }
      var dx = a.left - b.left, dy = a.top - b.top;
      if (dx || dy) r.animate([{ transform: 'translate(' + dx + 'px,' + dy + 'px)' }, { transform: 'none' }], { duration: 340, easing: 'cubic-bezier(.22,.8,.3,1)' });
    });
  };

  // ----- hub chooser: live load (polled every 10 s) -----
  function initHubs() {
    var box = document.getElementById('sb-hubs');
    if (!box || !box.dataset.loadsUrl || !window.fetch) return;
    var timer = null, stopped = false;
    function paint(h) {
      var card = box.querySelector('[data-hub-id="' + h.id + '"]');
      if (!card) return;
      var pct = Math.round(h.load * 100);
      var meter = card.querySelector('[data-meter]');
      if (meter) {
        meter.style.setProperty('--s', Math.max(0, Math.min(1, h.load)));
        meter.setAttribute('aria-valuenow', pct);
        meter.classList.toggle('is-busy', !!h.isBusy);
        meter.classList.toggle('is-quiet', !h.isBusy && !!h.isQuiet);
      }
      card.classList.toggle('is-busy', !!h.isBusy);
      var busyPill = card.querySelector('[data-hub-pill="busy"]'), quietPill = card.querySelector('[data-hub-pill="quiet"]');
      if (busyPill) busyPill.hidden = !h.isBusy;
      if (quietPill) quietPill.hidden = !(h.isQuiet && !h.isBusy);
      var open = card.querySelector('[data-hub-open]'), note = card.querySelector('[data-hub-busy-note]');
      if (open) open.hidden = !!h.isBusy;
      var closedBtn = card.querySelector('[data-hub-closed]');
      if (closedBtn) closedBtn.hidden = !h.isBusy;
      if (note) note.hidden = !h.isBusy;
      var pctEl = card.querySelector('[data-hub-pct]'), cntEl = card.querySelector('[data-hub-count]');
      var newPct = pct + '%', newCnt = h.busy + ' / ' + h.capacity;
      if (pctEl && pctEl.textContent.replace(/\s/g, '') !== newPct) { pctEl.textContent = newPct; if (!reduced) { pctEl.classList.remove('bump'); void pctEl.offsetWidth; pctEl.classList.add('bump'); } }
      if (cntEl && cntEl.textContent !== newCnt) { cntEl.textContent = newCnt; if (!reduced) { cntEl.classList.remove('bump'); void cntEl.offsetWidth; cntEl.classList.add('bump'); } }
    }
    function poll() {
      if (stopped || document.hidden) return;
      fetch(box.dataset.loadsUrl, { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } })
        .then(function (r) {
          var type = r.headers.get('content-type') || '';
          if (r.redirected || r.status === 401 || r.status === 403 || type.indexOf('json') === -1) { stopped = true; clearInterval(timer); return null; }
          return r.ok ? r.json() : null;
        })
        .then(function (list) { if (list) list.forEach(paint); })
        .catch(function () { /* offline or transient: try again on the next tick */ });
    }
    timer = setInterval(poll, parseInt(box.dataset.pollMs, 10) || 10000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) poll(); });
  }

  // ----- confetti (plain canvas, ~2 s) -----
  function confetti(canvas) {
    if (reduced || !canvas.getContext) { canvas.remove(); return; }
    var ctx = canvas.getContext('2d'), dpr = Math.min(window.devicePixelRatio || 1, 2);
    var W = canvas.width = window.innerWidth * dpr, H = canvas.height = window.innerHeight * dpr;
    var colors = ['#D43B10', '#F6C65B', '#7BB661', '#1F8A4D', '#F29A3C'];
    var parts = [];
    for (var i = 0; i < 90; i++) {
      parts.push({ x: W / 2 + (Math.random() - .5) * W * .2, y: H * .18, vx: (Math.random() - .5) * 12 * dpr, vy: (Math.random() * -9 - 2) * dpr,
        s: (Math.random() * 6 + 4) * dpr, r: Math.random() * 6.28, vr: (Math.random() - .5) * .4, c: colors[i % colors.length] });
    }
    var t0 = performance.now();
    (function frame(now) {
      var t = now - t0;
      if (t > 2400 || document.hidden && t > 600) { canvas.remove(); return; }
      ctx.clearRect(0, 0, W, H);
      ctx.globalAlpha = Math.max(0, 1 - t / 2400);
      parts.forEach(function (p) {
        p.vy += .32 * dpr; p.x += p.vx; p.y += p.vy; p.r += p.vr; p.vx *= .99;
        ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.fillStyle = p.c; ctx.fillRect(-p.s / 2, -p.s / 4, p.s, p.s / 2); ctx.restore();
      });
      raf(frame);
    })(t0);
  }

  // ----- skeletons for the dashboard until its data arrives -----
  function initSkeleton() {
    var stats = document.querySelectorAll('.sb-stat strong[id]');
    if (!stats.length) return;
    var charts = document.querySelectorAll('.sb-chart');
    function loaded(el) { return (el.textContent || '').trim() !== '\u2013' && (el.textContent || '').trim() !== ''; }
    function clear() {
      each(stats, function (s) { s.classList.remove('sb-skeleton'); });
      each(charts, function (c) { c.classList.remove('sb-skeleton'); });
    }
    each(stats, function (s) { if (!loaded(s)) s.classList.add('sb-skeleton'); });
    each(charts, function (c) { c.classList.add('sb-skeleton'); });
    each(stats, function (s) {
      new MutationObserver(function (_, obs) {
        if (loaded(s)) { obs.disconnect(); s.classList.remove('sb-skeleton'); countInText(s); if (Array.prototype.every.call(stats, loaded)) each(charts, function (c) { c.classList.remove('sb-skeleton'); }); }
      }).observe(s, { childList: true, characterData: true, subtree: true });
    });
    setTimeout(clear, 9000); // never leave a skeleton forever (offline / error)
  }

  // ----- header, progress bar, page transitions, ripple -----
  var progress = document.getElementById('sb-progress');
  function progStart() { if (!progress) return; progress.classList.remove('is-done'); void progress.offsetWidth; progress.classList.add('is-active'); }
  function progDone() { if (!progress) return; progress.classList.remove('is-active'); progress.classList.add('is-done'); }

  function boot() {
    var header = document.querySelector('.sb-header');
    if (header) {
      var onScroll = function () { header.classList.toggle('is-scrolled', window.scrollY > 8); };
      window.addEventListener('scroll', onScroll, { passive: true }); onScroll();
    }

    // Pause the ambient animations when the tab is hidden (saves battery).
    document.addEventListener('visibilitychange', function () { root.classList.toggle('sb-paused', document.hidden); });

    if (!reduced) {
      progStart();
      if (document.readyState === 'complete') progDone(); else window.addEventListener('load', progDone);

      document.addEventListener('submit', function (e) {
        if (e.defaultPrevented) return;
        progStart();
      });

      document.addEventListener('click', function (e) {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var a = e.target.closest && e.target.closest('a[href]');
        if (!a || a.target && a.target !== '_self' || a.hasAttribute('download') || a.hasAttribute('data-bs-toggle') || a.hasAttribute('data-no-transition')) return;
        var href = a.getAttribute('href') || '';
        if (!href || href.charAt(0) === '#' || /^(mailto:|tel:|javascript:)/i.test(href)) return;
        var url; try { url = new URL(a.href, location.href); } catch (err) { return; }
        if (url.origin !== location.origin || (url.pathname === location.pathname && url.search === location.search && url.hash)) return;
        e.preventDefault();
        root.classList.add('sb-leaving');
        progStart();
        setTimeout(function () { location.assign(url.href); }, 150);
      });

      document.addEventListener('pointerdown', function (e) {
        var b = e.target.closest && e.target.closest('.btn, .sb-chip');
        if (!b || b.disabled || b.classList.contains('disabled') || b.classList.contains('is-busy')) return;
        var r = b.getBoundingClientRect(), size = Math.max(r.width, r.height) * 2;
        var dot = document.createElement('span');
        dot.className = 'sb-ripple';
        dot.style.width = dot.style.height = size + 'px';
        dot.style.left = (e.clientX - r.left - size / 2) + 'px';
        dot.style.top = (e.clientY - r.top - size / 2) + 'px';
        b.appendChild(dot);
        dot.addEventListener('animationend', function () { dot.remove(); });
      }, { passive: true });
    }

    // Restore the page after the back button (bfcache) so it is never stuck half-faded.
    window.addEventListener('pageshow', function (e) {
      if (e.persisted) { root.classList.remove('sb-leaving'); progDone(); }
    });

    safe(armReveal);
    each(document.querySelectorAll('.sb-meter'), armMeter);
    each(document.querySelectorAll('[data-count]'), initCount);
    each(document.querySelectorAll('.sb-rule p'), countInText);
    safe(initHubs);
    safe(initSkeleton);

    var cel = document.querySelector('[data-celebrate]');
    if (cel) { var cv = cel.querySelector('[data-confetti]'); if (cv) setTimeout(function () { confetti(cv); }, 350); }

    // Meters / counters added later by other scripts (dashboard hub rows, etc.).
    if (window.MutationObserver) {
      new MutationObserver(function (list) {
        list.forEach(function (m) {
          each(m.addedNodes, function (n) {
            if (n.nodeType !== 1) return;
            if (n.matches && n.matches('.sb-meter')) armMeter(n);
            if (n.querySelectorAll) each(n.querySelectorAll('.sb-meter'), armMeter);
          });
        });
      }).observe(document.body, { childList: true, subtree: true });
    }
    window.__sbMotionReady = true;
  }

  window.__sbMotionReady = true; // mark early so the head failsafe does not fire while we start
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { safe(boot); });
  else safe(boot);
})();

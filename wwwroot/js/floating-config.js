// Shared floating position and drag behavior for grant/inventory configuration panels.
(function () {
  const STORAGE_KEY = 'dfo-gmtool.floatingConfigPosition';
  const EDGE_GAP = 16;
  const AVOID_GAP = 12;
  const panels = new Set();

  function readPosition() {
    try {
      const value = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
      if (value && Number.isFinite(value.right) && Number.isFinite(value.bottom))
        return { right: value.right, bottom: value.bottom };
    } catch (_) { }
    return null;
  }

  function savePosition(right, bottom) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ right, bottom }));
    } catch (_) { }
  }

  function clearPosition() {
    try { localStorage.removeItem(STORAGE_KEY); } catch (_) { }
  }

  function clamp(value, min, max) {
    return Math.min(Math.max(value, min), Math.max(min, max));
  }

  function applyPosition(panel, right, bottom) {
    panel.style.left = 'auto';
    panel.style.top = 'auto';
    panel.style.right = `${Math.round(right)}px`;
    panel.style.bottom = `${Math.round(bottom)}px`;
  }

  function panelIsRendered(panel) {
    return panel && !panel.classList.contains('hidden') && panel.getClientRects().length > 0;
  }

  function positionFits(panel, right, bottom) {
    const rect = panel.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0)
      return false;
    return right >= EDGE_GAP
      && bottom >= EDGE_GAP
      && right <= window.innerWidth - rect.width - EDGE_GAP
      && bottom <= window.innerHeight - rect.height - EDGE_GAP;
  }

  function defaultPosition(panel) {
    const rect = panel.getBoundingClientRect();
    const maxRight = window.innerWidth - rect.width - EDGE_GAP;
    const maxBottom = window.innerHeight - rect.height - EDGE_GAP;
    let right = EDGE_GAP;

    const avoidSelector = panel.dataset.floatingAvoid;
    const avoid = avoidSelector ? document.querySelector(avoidSelector) : null;
    if (avoid) {
      const avoidRect = avoid.getBoundingClientRect();
      const protectedRight = window.innerWidth - avoidRect.left + AVOID_GAP;
      if (protectedRight <= maxRight)
        right = protectedRight;
    }

    applyPosition(
      panel,
      clamp(right, EDGE_GAP, maxRight),
      clamp(EDGE_GAP, EDGE_GAP, maxBottom));
  }

  function restoreOrReset(panel) {
    if (!panelIsRendered(panel)) return;
    const saved = readPosition();
    if (saved && positionFits(panel, saved.right, saved.bottom)) {
      applyPosition(panel, saved.right, saved.bottom);
      return;
    }
    if (saved) clearPosition();
    defaultPosition(panel);
  }

  function ensureVisible(panel) {
    if (!panelIsRendered(panel) || panel.classList.contains('is-dragging')) return;
    const right = parseFloat(panel.style.right);
    const bottom = parseFloat(panel.style.bottom);
    if (!Number.isFinite(right) || !Number.isFinite(bottom)
        || !positionFits(panel, right, bottom)) {
      clearPosition();
      defaultPosition(panel);
    }
  }

  function init(panel) {
    if (!panel || panels.has(panel)) return;
    panels.add(panel);
    panel.classList.add('floating-config-panel');

    let drag = null;
    panel.addEventListener('pointerdown', (event) => {
      if (event.button !== 0 || !(event.target instanceof Element)) return;
      const handle = event.target.closest('.give-config-head');
      if (!handle || event.target.closest('button, input, select, textarea, a')) return;

      const rect = panel.getBoundingClientRect();
      drag = {
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        startRight: window.innerWidth - rect.right,
        startBottom: window.innerHeight - rect.bottom,
        moved: false,
      };
      panel.setPointerCapture(event.pointerId);
      panel.classList.add('is-dragging');
      event.preventDefault();
    });

    const moveDrag = (event) => {
      if (!drag || drag.pointerId !== event.pointerId) return;
      const dx = event.clientX - drag.startX;
      const dy = event.clientY - drag.startY;
      if (Math.abs(dx) + Math.abs(dy) > 3)
        drag.moved = true;

      const rect = panel.getBoundingClientRect();
      const right = clamp(
        drag.startRight - dx,
        EDGE_GAP,
        window.innerWidth - rect.width - EDGE_GAP);
      const bottom = clamp(
        drag.startBottom - dy,
        EDGE_GAP,
        window.innerHeight - rect.height - EDGE_GAP);
      applyPosition(panel, right, bottom);
      event.preventDefault();
    };

    const finishDrag = (event) => {
      if (!drag || drag.pointerId !== event.pointerId) return;
      const completedDrag = drag;
      drag = null;
      if (panel.hasPointerCapture(event.pointerId)) {
        try { panel.releasePointerCapture(event.pointerId); } catch (_) { }
      }
      panel.classList.remove('is-dragging');
      if (completedDrag.moved) {
        const right = parseFloat(panel.style.right);
        const bottom = parseFloat(panel.style.bottom);
        if (Number.isFinite(right) && Number.isFinite(bottom))
          savePosition(right, bottom);
      }
    };
    window.addEventListener('pointermove', moveDrag, { passive: false });
    window.addEventListener('pointerup', finishDrag);
    window.addEventListener('pointercancel', finishDrag);
    panel.addEventListener('lostpointercapture', finishDrag);

    if (window.ResizeObserver) {
      const observer = new ResizeObserver(() => requestAnimationFrame(() => ensureVisible(panel)));
      observer.observe(panel);
    }
  }

  function show(panel, options) {
    if (!panel) return;
    init(panel);
    panel.dataset.floatingAvoid = options?.avoidSelector || '';
    panel.classList.remove('hidden');
    requestAnimationFrame(() => restoreOrReset(panel));
  }

  function hide(panel) {
    if (panel) panel.classList.add('hidden');
  }

  function refresh(panel) {
    requestAnimationFrame(() => ensureVisible(panel));
  }

  window.addEventListener('resize', () => {
    for (const panel of panels)
      requestAnimationFrame(() => ensureVisible(panel));
  });

  window.FloatingConfigPanel = { show, hide, refresh };
})();

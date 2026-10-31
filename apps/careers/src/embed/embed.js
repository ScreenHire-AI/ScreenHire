/*
 * ScreenHire's careers widget (docs/architecture/careers-widget.md). Paste
 *
 *   <script src="https://{your careers address}/embed.js" async></script>
 *
 * where your jobs should appear. This script adds one frame after its own tag, showing your careers pages from
 * ScreenHire's careers origin, and makes the frame as tall as the page inside it. That is all it does: it reads nothing
 * from your page, and your page cannot read or change what candidates type in the frame. Your site must be on the
 * list of sites that can embed the careers page (Settings, Organization), or the browser refuses to show the frame.
 */
(function () {
  'use strict';
  var script = document.currentScript;
  if (!script || !script.src) return;

  // The careers board is this script's own address without "/embed.js".
  var board = script.src.replace(/[?#].*$/, '').replace(/\/embed\.js$/, '');
  var careersOrigin = new URL(board).origin;
  var source = new URL(board);
  source.searchParams.set('embed', window.location.origin);

  var frame = document.createElement('iframe');
  frame.className = 'screenhire-careers';
  frame.title = 'Jobs';
  frame.src = source.href;
  // Through the style object, never a style attribute, so a page whose policy forbids inline styles still works.
  frame.style.display = 'block';
  frame.style.width = '100%';
  frame.style.border = '0';
  frame.style.height = '600px';
  script.parentNode.insertBefore(frame, script.nextSibling);

  window.addEventListener('message', function (event) {
    // Only the careers origin, and only this frame: no other page or frame can resize it.
    if (event.origin !== careersOrigin || event.source !== frame.contentWindow) return;
    var data = event.data;
    if (!data || data.type !== 'screenhire:frame') return;
    var height = Math.ceil(Number(data.height));
    if (!isFinite(height) || height <= 0) return;
    frame.style.height = Math.min(height, 100000) + 'px';
    if (typeof data.title === 'string' && data.title) frame.title = data.title.slice(0, 200);
    // A new page in the frame starts at its top: bring that into view when the visitor had scrolled past it.
    if (data.page === true && frame.getBoundingClientRect().top < 0) frame.scrollIntoView();
  });
})();

// Injected into Swagger UI only when the API reference is exposed. It adds a topbar link to the standalone
// visit-access QR tool, which needs the camera permission Swagger UI cannot request itself (MP-97).
(function () {
  'use strict';

  var linkId = 'visit-access-tool-link';

  function addLink() {
    if (document.getElementById(linkId)) {
      return true;
    }

    var topbar = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
    if (!topbar) {
      return false;
    }

    var link = document.createElement('a');
    link.id = linkId;
    link.href = '/visit-access-tool.html';
    link.target = '_blank';
    link.rel = 'noopener';
    link.textContent = 'Visit access QR tool';
    link.style.color = '#fff';
    link.style.fontWeight = '600';
    link.style.fontSize = '14px';
    link.style.marginLeft = '24px';
    link.style.textDecoration = 'none';

    topbar.appendChild(link);
    return true;
  }

  if (!addLink()) {
    var attempts = 0;
    var timer = setInterval(function () {
      attempts += 1;
      if (addLink() || attempts >= 40) {
        clearInterval(timer);
      }
    }, 250);
  }
})();

// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Destructive forms opt into a confirmation prompt via data-confirm="…".
// (Handled here, not inline, so the strict Content-Security-Policy stays satisfied.)
(function () {
    "use strict";

    document.addEventListener("submit", function (event) {
        var form = event.target.closest ? event.target.closest("form[data-confirm]") : null;
        if (!form) {
            return;
        }
        if (!window.confirm(form.getAttribute("data-confirm"))) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);
})();

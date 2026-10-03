define(['baseView', 'emby-input', 'emby-button', 'emby-select', 'emby-checkbox', 'emby-scroller'], function (BaseView) {
    'use strict';

    // Emby runs a plugin page's own <script> blocks never, and loads page code through a controller module
    // (data-controller="__plugin/smartlistsjs" on the page). This controller loads the existing config-*.js
    // modules once, in order, and then lets config-init.js initialise the page as it did in the original plugin.
    var modules = [
        'config-core.js',
        'config-formatters.js',
        'config-schedules.js',
        'config-images.js',
        'config-sorts.js',
        'config-multi-select.js',
        'config-searchable-select.js',
        'config-user-select.js',
        'config-rules.js',
        'config-lists.js',
        'config-templates.js',
        'config-filters.js',
        'config-bulk-actions.js',
        'config-status.js',
        'config-api.js',
        'config-init.js'
    ];

    // Emby serves plugin resources with a heuristic public cache and no version in the URL, so a stale copy would
    // survive plugin upgrades; the stamp makes each page session fetch current scripts.
    var stamp = Date.now();
    var loadPromise = null;

    // The page styles are written against Emby's --jf-palette-* variables. Map them onto Emby's theme variables
    // (which follow the user's light/dark theme) so the existing styling keeps working.
    var THEME_STYLE_ID = 'smartlists-theme-variables';
    var THEME_CSS = [
        ':root {',
        '--jf-palette-background-default: hsl(var(--background-hue), var(--background-saturation), var(--background-lightness));',
        '--jf-palette-background-paper: hsl(var(--card-background-hue), var(--card-background-saturation), var(--card-background-lightness));',
        '--jf-palette-text-primary: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), var(--theme-text-color-alpha));',
        '--jf-palette-text-secondary: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), var(--theme-secondary-text-color-alpha));',
        '--jf-palette-text-disabled: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .38);',
        '--jf-palette-divider: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .14);',
        '--jf-palette-action-hover: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .08);',
        '--jf-palette-action-focus: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .12);',
        '--jf-palette-action-disabled: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .3);',
        '--jf-palette-primary: hsl(var(--theme-primary-color-hue), var(--theme-primary-color-saturation), var(--theme-primary-color-lightness));',
        '--jf-palette-primary-main: hsl(var(--theme-primary-color-hue), var(--theme-primary-color-saturation), var(--theme-primary-color-lightness));',
        '--jf-palette-error-main: #f44336;',
        '--jf-palette-error-light: #e57373;',
        '--jf-palette-error-contrastText: #fff;',
        '--jf-palette-warning-main: #ffa726;',
        '--jf-palette-success-main: #66bb6a;',
        '--jf-palette-success-dark: #388e3c;',
        '--jf-palette-common-white: #fff;',
        '--jf-palette-LinearProgress-warningBg: rgba(255, 167, 38, .3);',
        '--jf-palette-Alert-warningColor: #ffa726;',
        '}'
    ].join('\n');

    function ensureThemeVariables() {
        if (!document.getElementById(THEME_STYLE_ID)) {
            var style = document.createElement('style');
            style.id = THEME_STYLE_ID;
            style.textContent = THEME_CSS;
            document.head.appendChild(style);
        }
    }

    function loadScript(name) {
        return new Promise(function (resolve, reject) {
            var script = document.createElement('script');
            script.async = false;
            script.src = 'configurationpage?name=' + name + '&t=' + stamp;
            script.onload = function () {
                this.remove();
                resolve();
            };
            script.onerror = function () {
                this.remove();
                reject(new Error('SmartLists: failed to load ' + name));
            };
            document.head.appendChild(script);
        });
    }

    function loadModules() {
        if (!loadPromise) {
            loadPromise = modules.reduce(function (chain, name) {
                return chain.then(function () {
                    return loadScript(name);
                });
            }, Promise.resolve()).catch(function (err) {
                loadPromise = null;
                throw err;
            });
        }
        return loadPromise;
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        ensureThemeVariables();

        view.addEventListener('viewshow', function () {
            loadModules().then(function () {
                window.SmartLists.activePage = view;
                // config-init.js initialises the page from the document-level pageshow event.
                document.dispatchEvent(new Event('pageshow'));
            }).catch(function (err) {
                console.error(err);
            });
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    return View;
});

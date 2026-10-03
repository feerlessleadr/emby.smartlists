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
        '}',
        // Emby's .emby-select-withcolor forces white text (it is meant for dark pill selects), which is unreadable on
        // the light theme; keep the custom selects on the theme text colour and give their dropdowns a solid fill.
        '.SmartListsConfigurationPage .searchable-select-display, .SmartListsConfigurationPage .searchable-select-display-text,',
        '.SmartListsConfigurationPage .multi-select-display, .SmartListsConfigurationPage .multi-select-display * {',
        'color: var(--jf-palette-text-primary) !important;',
        '}',
        // Layout polish for Emby (the page was designed against Jellyfin's taller controls and card colours).
        // Two surface tints derived from the theme text colour work in both the light and the dark theme.
        '.SmartListsConfigurationPage {',
        '--sl-surface-1: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .06);',
        '--sl-surface-2: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .12);',
        '--sl-control-height: 2.4em;',
        '}',
        // One look for every field: the custom dropdowns (field picker, media types, users) had no box at all and
        // the sort selects blended into their panel, so they read as floating text. Give all controls the same
        // fill, border and corners as the theme's input colour.
        '.SmartListsConfigurationPage {',
        '--sl-input-bg: hsl(var(--input-background-hue), var(--input-background-saturation), var(--input-background-lightness));',
        '--sl-input-border: hsla(var(--theme-text-color-hue), var(--theme-text-color-saturation), var(--theme-text-color-lightness), .28);',
        '}',
        '.SmartListsConfigurationPage select.emby-select, .SmartListsConfigurationPage .searchable-select-display,',
        '.SmartListsConfigurationPage .multi-select-display, .SmartListsConfigurationPage input.emby-input,',
        '.SmartListsConfigurationPage textarea.emby-input, .SmartListsConfigurationPage .tag-input-container.emby-input {',
        'background: var(--sl-input-bg) !important;',
        'border: 1px solid var(--sl-input-border) !important;',
        'border-radius: 4.3px;',
        '}',
        '.SmartListsConfigurationPage .searchable-select-display, .SmartListsConfigurationPage .multi-select-display {',
        'padding-left: 9px !important;',
        'padding-right: 9px !important;',
        'justify-content: space-between;',
        '}',
        '.SmartListsConfigurationPage .selectContainer {',
        'width: 100%;',
        '}',
        // Free-text areas: keep the text off the top edge like the single-line inputs.
        '.SmartListsConfigurationPage textarea.emby-input {',
        'padding-top: 0.55em !important;',
        'padding-bottom: 0.55em !important;',
        'line-height: 1.4;',
        '}',
        // Breathing room between a field label, the checkbox under it and the dropdown below.
        '.SmartListsConfigurationPage label.inputLabel {',
        'margin-bottom: 0.9em !important;',
        '}',
        '.SmartListsConfigurationPage .inputContainer:has([id$="MultiSelect"]) > label.inputLabel {',
        'margin-bottom: 1.4em !important;',
        '}',
        '.SmartListsConfigurationPage [id$="MultiSelect"] {',
        'margin-top: 0.5em;',
        '}',
        '.SmartListsConfigurationPage [id$="MultiSelect"] label.emby-checkbox-label {',
        'margin-bottom: 0.6em;',
        '}',
        '.SmartListsConfigurationPage .searchable-select-display-text, .SmartListsConfigurationPage .multi-select-display-text,',
        '.SmartListsConfigurationPage .multi-select-placeholder, .SmartListsConfigurationPage .multi-select-selected {',
        'font-size: inherit !important;',
        '}',
        // Dropdowns as tall as the text inputs.
        '.SmartListsConfigurationPage select.emby-select, .SmartListsConfigurationPage .searchable-select-display,',
        '.SmartListsConfigurationPage .multi-select-display {',
        'min-height: var(--sl-control-height) !important;',
        'box-sizing: border-box;',
        'display: flex;',
        'align-items: center;',
        'padding-top: 0 !important;',
        'padding-bottom: 0 !important;',
        '}',
        '.SmartListsConfigurationPage select.emby-select { display: block; }',
        // Tab bar with its own background and a clearly highlighted active tab.
        '.SmartListsConfigurationPage .localnav {',
        'display: inline-flex !important;',
        'max-width: 100%;',
        'flex-wrap: wrap;',
        'gap: 0;',
        'background: var(--sl-surface-2);',
        'border-radius: 6px;',
        'overflow: hidden;',
        'margin: 0 0 1.25em 0 !important;',
        '}',
        '.SmartListsConfigurationPage .localnav .emby-button {',
        'margin: 0 !important;',
        'border-radius: 0 !important;',
        'background: transparent;',
        '}',
        '.SmartListsConfigurationPage .localnav .emby-button.ui-btn-active {',
        'background: var(--jf-palette-primary-main) !important;',
        'color: #fff !important;',
        '}',
        // Rule groups, group options and sort boxes as distinct panels instead of one big box.
        '.SmartListsConfigurationPage .logic-group, .SmartListsConfigurationPage .sort-box {',
        'background: var(--sl-surface-1) !important;',
        'border: 1px solid var(--jf-palette-divider) !important;',
        'border-radius: 6px;',
        'margin-bottom: 1.25em !important;',
        '}',
        '.SmartListsConfigurationPage .logic-group .rule-row {',
        'background: var(--sl-surface-1);',
        'border-radius: 6px;',
        'padding: 0.5em;',
        'margin-bottom: 0.75em;',
        '}',
        '.SmartListsConfigurationPage .group-max-items-container {',
        'background: var(--sl-surface-2) !important;',
        'border-radius: 6px;',
        '}',
        '.SmartListsConfigurationPage #rules-container, .SmartListsConfigurationPage #sorts-container {',
        'margin-bottom: 1.5em;',
        '}',
        '.SmartListsConfigurationPage .searchable-select-dropdown, .SmartListsConfigurationPage .multi-select-dropdown {',
        'background: var(--jf-palette-background-default) !important;',
        'backdrop-filter: none !important;',
        'border: 1px solid var(--jf-palette-divider);',
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

                // Make the dropdowns exactly as tall as the text inputs, whatever the theme/zoom.
                requestAnimationFrame(function () {
                    var input = view.querySelector('#playlistName');
                    var height = input ? input.getBoundingClientRect().height : 0;
                    if (height > 0) {
                        view.style.setProperty('--sl-control-height', Math.round(height) + 'px');
                    }
                });
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

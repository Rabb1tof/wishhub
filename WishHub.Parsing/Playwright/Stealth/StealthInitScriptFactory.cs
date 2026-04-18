using System.Text.Json;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Parsing.Playwright.Stealth;

public sealed class StealthInitScriptFactory : IStealthInitScriptFactory
{
    private static readonly object[] Plugins =
    [
        new
        {
            name = "PDF Viewer",
            filename = "internal-pdf-viewer",
            description = "Portable Document Format"
        },
        new
        {
            name = "Chrome PDF Viewer",
            filename = "internal-pdf-viewer",
            description = "Portable Document Format"
        },
        new
        {
            name = "Chromium PDF Viewer",
            filename = "internal-pdf-viewer",
            description = "Portable Document Format"
        },
        new
        {
            name = "Widevine Content Decryption Module",
            filename = "widevinecdmadapter.dll",
            description = "Enables Widevine licenses for playback of HTML audio/video content"
        }
    ];

    private static readonly object[] MimeTypes =
    [
        new
        {
            type = "application/pdf",
            suffixes = "pdf",
            description = "Portable Document Format"
        },
        new
        {
            type = "application/x-google-chrome-pdf",
            suffixes = "pdf",
            description = "Portable Document Format"
        },
        new
        {
            type = "application/x-ppapi-widevine-cdm",
            suffixes = string.Empty,
            description = "Widevine Content Decryption Module"
        }
    ];

    public string Create(FingerprintProfile profile)
    {
        var languagesJson = JsonSerializer.Serialize(new[] { "ru-RU", "ru", "en-US", "en" });
        var pluginsJson = JsonSerializer.Serialize(Plugins);
        var mimeTypesJson = JsonSerializer.Serialize(MimeTypes);
        var platformJson = JsonSerializer.Serialize(profile.Platform);
        var webGlVendorJson = JsonSerializer.Serialize(profile.WebGLVendor);
        var webGlRendererJson = JsonSerializer.Serialize(profile.WebGLRenderer);

        return $$"""
(() => {
    const languages = {{languagesJson}};
    const pluginData = {{pluginsJson}};
    const mimeTypeData = {{mimeTypesJson}};
    const platform = {{platformJson}};
    const webGlVendor = {{webGlVendorJson}};
    const webGlRenderer = {{webGlRendererJson}};

    const createArrayLike = (items, tagName) => {
        const values = items.map((item, index) => Object.assign(Object.create(null), item, { index }));
        values.item = (index) => values[index] ?? null;
        values.namedItem = (name) => values.find((value) => value.name === name || value.type === name) ?? null;
        values.refresh = () => undefined;
        Object.defineProperty(values, Symbol.toStringTag, { value: tagName, enumerable: false });
        return values;
    };

    Object.defineProperty(Navigator.prototype, 'webdriver', {
        configurable: true,
        get: () => undefined
    });

    Object.defineProperty(navigator, 'plugins', {
        configurable: true,
        get: () => createArrayLike(pluginData, 'PluginArray')
    });

    Object.defineProperty(navigator, 'mimeTypes', {
        configurable: true,
        get: () => createArrayLike(mimeTypeData, 'MimeTypeArray')
    });

    Object.defineProperty(navigator, 'languages', {
        configurable: true,
        get: () => languages
    });

    Object.defineProperty(navigator, 'hardwareConcurrency', {
        configurable: true,
        get: () => {{profile.HardwareConcurrency}}
    });

    Object.defineProperty(navigator, 'deviceMemory', {
        configurable: true,
        get: () => {{profile.DeviceMemory}}
    });

    Object.defineProperty(navigator, 'platform', {
        configurable: true,
        get: () => platform
    });

    window.chrome = window.chrome || {
        runtime: {},
        loadTimes: function () { return {}; },
        csi: function () { return {}; }
    };

    const originalQuery = navigator.permissions && navigator.permissions.query
        ? navigator.permissions.query.bind(navigator.permissions)
        : null;

    if (originalQuery) {
        navigator.permissions.query = (parameters) => {
            if (parameters && parameters.name === 'notifications') {
                return Promise.resolve({
                    state: Notification.permission,
                    onchange: null
                });
            }

            return originalQuery(parameters);
        };
    }

    const overrideWebGl = (target) => {
        if (!target || !target.prototype || typeof target.prototype.getParameter !== 'function') {
            return;
        }

        const originalGetParameter = target.prototype.getParameter;
        target.prototype.getParameter = function (parameter) {
            if (parameter === 37445) {
                return webGlVendor;
            }

            if (parameter === 37446) {
                return webGlRenderer;
            }

            return originalGetParameter.call(this, parameter);
        };
    };

    overrideWebGl(window.WebGLRenderingContext);
    overrideWebGl(window.WebGL2RenderingContext);
})();
""";
    }
}

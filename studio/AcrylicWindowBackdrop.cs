using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace Monolith.Studio;

internal sealed class AcrylicWindowBackdrop : IDisposable
{
    private readonly DesktopAcrylicController _controller;
    private readonly SystemBackdropConfiguration _configuration;
    private readonly ICompositionSupportsSystemBackdrop _target;

    private AcrylicWindowBackdrop(DesktopAcrylicController controller, SystemBackdropConfiguration configuration, ICompositionSupportsSystemBackdrop target)
    {
        _controller = controller;
        _configuration = configuration;
        _target = target;
    }

    public static AcrylicWindowBackdrop? Attach(Window window, double tintOpacity)
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            App.LogStartup("Desktop Acrylic is not supported by this Windows version.");
            return null;
        }

        try
        {
            DispatcherQueue.GetForCurrentThread()?.EnsureSystemDispatcherQueue();
            var target = window.As<ICompositionSupportsSystemBackdrop>();
            var configuration = new SystemBackdropConfiguration
            {
                // The bar is intentionally WS_EX_NOACTIVATE, so WinUI never
                // observes it as active. Keep the acrylic material in its
                // active state while Monolith is running.
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };
            var controller = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Base,
                TintColor = Color.FromArgb(255, 22, 22, 22),
                TintOpacity = (float)Math.Clamp(tintOpacity, 0.08, 0.5),
                FallbackColor = Color.FromArgb(255, 35, 35, 35)
            };
            controller.AddSystemBackdropTarget(target);
            controller.SetSystemBackdropConfiguration(configuration);
            return new AcrylicWindowBackdrop(controller, configuration, target);
        }
        catch (Exception ex)
        {
            App.LogStartup("Unable to attach the Desktop Acrylic controller", ex);
            return null;
        }
    }

    public void SetTintOpacity(double value) => _controller.TintOpacity = (float)Math.Clamp(value, 0.08, 0.5);

    public void Dispose()
    {
        try { _controller.RemoveSystemBackdropTarget(_target); }
        catch { }
        _controller.Dispose();
    }
}

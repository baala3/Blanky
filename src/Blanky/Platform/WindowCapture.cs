using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Blanky;

/// <summary>
/// Screenshots a window exactly as it looks on screen (Mica included), for UI
/// review and website images. Used by the <c>--capture</c> launch flag.
/// </summary>
internal static class WindowCapture
{
    private const int ExtendedFrameBounds = 9;
    private const uint RenderFullContent = 2; // PW_RENDERFULLCONTENT: includes DirectComposition (XAML) content

    public static async Task SaveAsync(Window window, string path)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        NativeMethods.SetForegroundWindow(hwnd);
        await Task.Delay(300);
        // The visible frame is inset from the window rect (invisible resize borders).
        NativeMethods.DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out var frame, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Rect>());
        NativeMethods.GetWindowRect(hwnd, out var windowRect);
        int fullWidth = windowRect.Right - windowRect.Left;
        int fullHeight = windowRect.Bottom - windowRect.Top;
        int width = frame.Right - frame.Left;
        int height = frame.Bottom - frame.Top;

        var screen = NativeMethods.GetDC(0);
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screen, fullWidth, fullHeight);
        var previous = NativeMethods.SelectObject(memory, bitmap);
        var full = new byte[fullWidth * fullHeight * 4];
        var pixels = new byte[width * height * 4];
        try
        {
            // PrintWindow renders the window itself, so other windows covering it don't show up.
            NativeMethods.PrintWindow(hwnd, memory, RenderFullContent);
            NativeMethods.SelectObject(memory, previous);
            var header = new NativeMethods.BitmapInfoHeader
            {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = fullWidth,
                Height = -fullHeight, // top-down rows
                Planes = 1,
                BitCount = 32,
            };
            NativeMethods.GetDIBits(memory, bitmap, 0, (uint)fullHeight, full, ref header, 0);

            // Crop to the visible frame.
            int offsetX = frame.Left - windowRect.Left;
            int offsetY = frame.Top - windowRect.Top;
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(full, (((row + offsetY) * fullWidth) + offsetX) * 4, pixels, row * width * 4, width * 4);
            }
        }
        finally
        {
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memory);
            _ = NativeMethods.ReleaseDC(0, screen);
        }

        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}

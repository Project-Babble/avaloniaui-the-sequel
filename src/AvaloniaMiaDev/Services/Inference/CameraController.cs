using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Helpers;
using AvaloniaMiaDev.Services.Inference.Enums;
using AvaloniaMiaDev.Services.Inference.Models;
using AvaloniaMiaDev.Views;

namespace AvaloniaMiaDev.Services.Inference;

public class CameraController : IDisposable
{
    private readonly HomePageView _view;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IInferenceService _inferenceService;
    private readonly Camera _camera;

    // UI Elements
    private readonly Rectangle _rectangleWindow;
    private readonly Button _selectEntireFrameButton;
    private readonly Viewbox _viewBox;
    private readonly Image _mouthWindow;
    private readonly Canvas _canvas;

    // State
    private CamViewMode _camViewMode = CamViewMode.Tracking;
    private Rect _overlayRectangle;
    private bool _isCropping;
    private double _dragStartX;
    private double _dragStartY;
    private WriteableBitmap _bitmap;

    // Settings keys
    private readonly string _roiSettingKeyX;
    private readonly string _roiSettingKeyY;
    private readonly string _roiSettingKeyWidth;
    private readonly string _roiSettingKeyHeight;
    private readonly string _rotationSettingKey;
    private readonly string _flipXSettingKey;
    private readonly string _flipYSettingKey;

    // Tracking mode property
    private readonly StyledProperty<bool> _isTrackingModeProperty;

    // MJPEG Streaming
    private HttpListener _httpListener;
    private bool _isStreaming;
    private CancellationTokenSource _streamingCancellationTokenSource;
    private readonly object _streamLock = new object();
    private byte[] _currentJpegFrame;
    private int _mjpegPort = 8080;
    private readonly string _mjpegBoundary = "mjpegstream";

    public CameraController(
        HomePageView view,
        ILocalSettingsService localSettingsService,
        IInferenceService inferenceService,
        Camera camera,
        Rectangle rectangleWindow,
        Button selectEntireFrameButton,
        Viewbox viewBox,
        Image mouthWindow,
        Canvas canvas,
        string roiSettingKeyX,
        string roiSettingKeyY,
        string roiSettingKeyWidth,
        string roiSettingKeyHeight,
        string rotationSettingKey,
        string flipXSettingKey,
        string flipYSettingKey,
        StyledProperty<bool> isTrackingModeProperty)
    {
        _view = view;
        _localSettingsService = localSettingsService;
        _inferenceService = inferenceService;
        _camera = camera;
        _rectangleWindow = rectangleWindow;
        _selectEntireFrameButton = selectEntireFrameButton;
        _viewBox = viewBox;
        _mouthWindow = mouthWindow;
        _canvas = canvas;
        _roiSettingKeyX = roiSettingKeyX;
        _roiSettingKeyY = roiSettingKeyY;
        _roiSettingKeyWidth = roiSettingKeyWidth;
        _roiSettingKeyHeight = roiSettingKeyHeight;
        _rotationSettingKey = rotationSettingKey;
        _flipXSettingKey = flipXSettingKey;
        _flipYSettingKey = flipYSettingKey;
        _isTrackingModeProperty = isTrackingModeProperty;

        // Set up event handlers
        _mouthWindow.PointerPressed += OnPointerPressed;
        _mouthWindow.PointerMoved += OnPointerMoved;
        _mouthWindow.PointerReleased += OnPointerReleased;

        // Configure rectangle
        _rectangleWindow.Stroke = Brushes.Red;
        _rectangleWindow.StrokeThickness = 2;

        // Set initial mode
        _view.SetValue(_isTrackingModeProperty, true);

        // Initialize MJPEG streaming
        _currentJpegFrame = new byte[0];
    }

    public async Task UpdateImage(bool isVisible)
    {
        var isCroppingModeUiVisible = _camViewMode == CamViewMode.Cropping;
        _rectangleWindow.IsVisible = isCroppingModeUiVisible;
        _selectEntireFrameButton.IsVisible = isCroppingModeUiVisible;
        _viewBox.MaxHeight = isCroppingModeUiVisible ? double.MaxValue : 192;
        _viewBox.MaxWidth = isCroppingModeUiVisible ? double.MaxValue : 192;

        bool valid;
        bool useColor;
        byte[]? image;
        (int width, int height) dims;

        if (_overlayRectangle is { X: 0, Y: 0, Width: 0, Height: 0 })
        {
            var x = await _localSettingsService.ReadSettingAsync<double>(_roiSettingKeyX);
            var y = await _localSettingsService.ReadSettingAsync<double>(_roiSettingKeyY);
            var width = await _localSettingsService.ReadSettingAsync<double>(_roiSettingKeyWidth);
            var height = await _localSettingsService.ReadSettingAsync<double>(_roiSettingKeyHeight);
            _overlayRectangle = new Rect(x, y, width, height);
        }

        var cameraSettings = new CameraSettings
        {
            Camera = _camera,
            RoiX = (int)_overlayRectangle.X,
            RoiY = (int)_overlayRectangle.Y,
            RoiWidth = (int)_overlayRectangle.Width,
            RoiHeight = (int)_overlayRectangle.Height,
            RotationRadians = await _localSettingsService.ReadSettingAsync<float>(_rotationSettingKey),
            UseHorizontalFlip = await _localSettingsService.ReadSettingAsync<bool>(_flipXSettingKey),
            UseVerticalFlip = await _localSettingsService.ReadSettingAsync<bool>(_flipYSettingKey),
            Brightness = 1f
        };

        switch (_camViewMode)
        {
            case CamViewMode.Tracking:
                useColor = false;
                valid = _inferenceService.GetImage(cameraSettings, out image, out dims);
                break;
            case CamViewMode.Cropping:
                useColor = true;
                valid = _inferenceService.GetRawImage(cameraSettings, ColorType.Bgr24, out image, out dims);
                break;
            default:
                return;
        }

        if (valid && isVisible)
        {
            _viewBox.Margin = new Thickness(0, 0, 0, 16);

            if (dims.width == 0 || dims.height == 0 || image is null ||
                double.IsNaN(_mouthWindow.Width) || double.IsNaN(_mouthWindow.Height))
            {
                ResetViewSizes();
                return;
            }

            if (cameraSettings.RoiWidth == 0 || cameraSettings.RoiHeight == 0)
            {
                SelectEntireFrame();
            }

            // Create or update bitmap if needed
            if (_bitmap is null ||
                _bitmap.PixelSize.Width != dims.width ||
                _bitmap.PixelSize.Height != dims.height)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(dims.width, dims.height),
                    new Vector(96, 96),
                    useColor ? PixelFormats.Bgr24 : PixelFormats.Gray8,
                    AlphaFormat.Opaque);
                _mouthWindow.Source = _bitmap;
            }

            using var frameBuffer = _bitmap.Lock();
            {
                Marshal.Copy(image, 0, frameBuffer.Address, image.Length);
            }

            // Update MJPEG frame
            UpdateMjpegFrame();

            if (_mouthWindow.Width != dims.width || _mouthWindow.Height != dims.height)
            {
                _mouthWindow.Width = dims.width;
                _mouthWindow.Height = dims.height;
                _canvas.Width = dims.width;
                _canvas.Height = dims.height;
            }

            _rectangleWindow.Width = _overlayRectangle.Width;
            _rectangleWindow.Height = _overlayRectangle.Height;
            Canvas.SetLeft(_rectangleWindow, _overlayRectangle.X);
            Canvas.SetTop(_rectangleWindow, _overlayRectangle.Y);
        }
        else
        {
            ResetViewSizes();
        }

        Dispatcher.UIThread.Post(_mouthWindow.InvalidateVisual, DispatcherPriority.Render);
        Dispatcher.UIThread.Post(_canvas.InvalidateVisual, DispatcherPriority.Render);
        Dispatcher.UIThread.Post(_rectangleWindow.InvalidateVisual, DispatcherPriority.Render);
    }

    private void ResetViewSizes()
    {
        _viewBox.Margin = new Thickness();
        _mouthWindow.Width = 0;
        _mouthWindow.Height = 0;
        _canvas.Width = 0;
        _canvas.Height = 0;
        _rectangleWindow.Width = 0;
        _rectangleWindow.Height = 0;
    }

    public WriteableBitmap Bitmap => _bitmap;

    public void StartCamera(string cameraAddress)
    {
        _inferenceService.ConfigurePlatformConnectors(_camera, cameraAddress);
    }

    public void StopCamera(Camera camera)
    {
        _inferenceService.Shutdown(camera);
    }

    public void SetTrackingMode()
    {
        _camViewMode = CamViewMode.Tracking;
        _isCropping = false;
        _view.SetValue(_isTrackingModeProperty, true);
        OnPointerReleased(null, null!); // Close and save any open crops
    }

    public void SetCroppingMode()
    {
        _camViewMode = CamViewMode.Cropping;
        _view.SetValue(_isTrackingModeProperty, false);
    }

    public void SelectEntireFrame()
    {
        if (_bitmap is null) return;

        _overlayRectangle = new Rect(0, 0, _bitmap.Size.Width, _bitmap.Size.Height);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_camViewMode != CamViewMode.Cropping) return;

        var position = e.GetPosition(_mouthWindow);
        _dragStartX = position.X;
        _dragStartY = position.Y;

        _isCropping = true;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isCropping) return;

        Image? image = sender as Image;

        var position = e.GetPosition(_mouthWindow);

        double x, y, w, h;

        if (position.X < _dragStartX)
        {
            x = position.X;
            w = _dragStartX - x;
        }
        else
        {
            x = _dragStartX;
            w = position.X - _dragStartX;
        }

        if (position.Y < _dragStartY)
        {
            y = position.Y;
            h = _dragStartY - y;
        }
        else
        {
            y = _dragStartY;
            h = position.Y - _dragStartY;
        }

        _overlayRectangle = new Rect(x, y, Math.Min(image!.Width, w), Math.Min(image.Height, h));
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isCropping) return;

        _isCropping = false;

        await _localSettingsService.SaveSettingAsync<double>(_roiSettingKeyX, _overlayRectangle.X);
        await _localSettingsService.SaveSettingAsync<double>(_roiSettingKeyY, _overlayRectangle.Y);
        await _localSettingsService.SaveSettingAsync<double>(_roiSettingKeyWidth, _overlayRectangle.Width);
        await _localSettingsService.SaveSettingAsync<double>(_roiSettingKeyHeight, _overlayRectangle.Height);
    }

    #region MJPEG Streaming

    /// <summary>
    /// Start MJPEG streaming server on the specified port
    /// </summary>
    /// <param name="port">Port to listen on (default: 8080)</param>
    public void StartMjpegStreaming(int port = 8080)
    {
        if (_isStreaming)
            return;

        _mjpegPort = port;
        _isStreaming = true;
        _streamingCancellationTokenSource = new CancellationTokenSource();

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_mjpegPort}/");
            _httpListener.Start();

            Task.Run(() => HandleHttpRequests(_streamingCancellationTokenSource.Token));
        }
        catch (Exception ex)
        {
            _isStreaming = false;
        }
    }

    /// <summary>
    /// Stop the MJPEG streaming server
    /// </summary>
    public void StopMjpegStreaming()
    {
        if (!_isStreaming)
            return;

        _isStreaming = false;
        _streamingCancellationTokenSource?.Cancel();

        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch (Exception ex)
        {

        }
    }

    private async Task HandleHttpRequests(CancellationToken cancellationToken)
    {
        while (_isStreaming && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (cancellationToken.IsCancellationRequested)
                    break;

                string requestPath = context.Request.Url.AbsolutePath.ToLowerInvariant();

                if (requestPath == "/mjpeg")
                {
                    // Handle MJPEG stream request
                    Task.Run(() => HandleMjpegRequest(context, cancellationToken));
                }
                else if (requestPath == "/snapshot" || requestPath == "/jpeg")
                {
                    // Handle single JPEG snapshot request
                    Task.Run(() => HandleSnapshotRequest(context));
                }
                else
                {
                    // Handle other requests (like a simple status page)
                    HandleDefaultRequest(context);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {

                }
            }
        }
    }

    private async Task HandleMjpegRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerResponse response = context.Response;

        try
        {
            response.ContentType = $"multipart/x-mixed-replace; boundary={_mjpegBoundary}";
            response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Headers.Add("Pragma", "no-cache");
            response.Headers.Add("Expires", "0");

            using (var outputStream = response.OutputStream)
            {
                while (_isStreaming && !cancellationToken.IsCancellationRequested)
                {
                    byte[] frameData;

                    if (_currentJpegFrame.Length == 0)
                    {
                        await Task.Delay(33, cancellationToken); // ~30fps
                        continue;
                    }

                    frameData = _currentJpegFrame;

                    try
                    {
                        // Write multipart boundary
                        string header = $"\r\n--{_mjpegBoundary}\r\n" +
                                        "Content-Type: image/jpeg\r\n" +
                                        $"Content-Length: {frameData.Length}\r\n\r\n";

                        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                        await outputStream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken);

                        // Write JPEG data
                        await outputStream.WriteAsync(frameData, 0, frameData.Length, cancellationToken);
                        await outputStream.FlushAsync(cancellationToken);

                        // Control frame rate
                        await Task.Delay(33, cancellationToken); // ~30fps
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {

        }
        finally
        {
            response.Close();
        }
    }

    private void HandleSnapshotRequest(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;

        try
        {
            response.ContentType = "image/jpeg";
            response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Headers.Add("Pragma", "no-cache");
            response.Headers.Add("Expires", "0");

            byte[] frameData;

            lock (_streamLock)
            {
                frameData = _currentJpegFrame;
            }

            if (frameData.Length > 0)
            {
                response.ContentLength64 = frameData.Length;
                response.OutputStream.Write(frameData, 0, frameData.Length);
            }
            else
            {
                response.StatusCode = 503; // Service Unavailable
                byte[] errorMsg = Encoding.UTF8.GetBytes("No image available");
                response.ContentLength64 = errorMsg.Length;
                response.OutputStream.Write(errorMsg, 0, errorMsg.Length);
            }
        }
        catch (Exception ex)
        {

        }
        finally
        {
            response.Close();
        }
    }

    private void HandleDefaultRequest(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;

        try
        {
            string html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Camera Stream</title>
                    <style>
                        body {{ font-family: Arial, sans-serif; margin: 20px; }}
                        h1 {{ color: #333; }}
                        .stream-container {{ margin-top: 20px; }}
                        img {{ max-width: 100%; border: 1px solid #ccc; }}
                    </style>
                </head>
                <body>
                    <h1>Camera Stream</h1>
                    <div class='stream-container'>
                        <h2>Live Stream</h2>
                        <img src='/mjpeg' alt='MJPEG Stream'>
                    </div>
                    <div class='stream-container'>
                        <h2>Static Image</h2>
                        <img src='/snapshot' alt='Snapshot'>
                    </div>
                </body>
                </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {

        }
        finally
        {
            response.Close();
        }
    }

    private void UpdateMjpegFrame()
    {
        if (_bitmap == null || !_isStreaming)
            return;

        try
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                // Update the current frame
                lock (_streamLock)
                {
                    // Save the current bitmap as JPEG
                    _bitmap.Save(memoryStream);
                    _currentJpegFrame = memoryStream.ToArray();
                }
            }
        }
        catch (Exception ex)
        {

        }
    }

    #endregion

    public void Dispose()
    {
        StopMjpegStreaming();

        _mouthWindow.PointerPressed -= OnPointerPressed;
        _mouthWindow.PointerMoved -= OnPointerMoved;
        _mouthWindow.PointerReleased -= OnPointerReleased;

        _bitmap?.Dispose();
        _streamingCancellationTokenSource?.Dispose();
    }
}

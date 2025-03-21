﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Services.Inference.Captures;
using AvaloniaMiaDev.Services.Inference.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AvaloniaMiaDev.Services.Inference.Platforms;

/// <summary>
/// Manages what Captures are allowed to run on what platforms, as well as their Urls, etc.
/// </summary>
public abstract class PlatformConnector
{
    protected ILogger Logger { get; }
    protected ILocalSettingsService LocalSettingsService { get; }

    /// <summary>
    /// The path to where the "data" lies
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// A Platform may have many Capture sources, but only one may ever be active at a time.
    /// This represents the current (and a valid) Capture source for this Platform
    /// </summary>
    public Capture? Capture { get; private set; }

    /// <summary>
    /// Dynamic collection of Capture types, their identifying strings as well as prefix/suffix controls
    /// Add (or remove) from this collection to support platform specific connectors at runtime
    /// Or support weird hardware setups
    /// </summary>
    public Dictionary<(HashSet<string> strings, bool areSuffixes), Type> Captures;

    protected abstract Type DefaultCapture { get; }

    private uint _lastFrameCount = 0;

    public PlatformConnector(string url, ILogger logger, ILocalSettingsService localSettingsService)
    {
        Url = url;
        Logger = logger;
        LocalSettingsService = localSettingsService;
    }

    /// <summary>
    /// Initializes a Platform Connector
    /// </summary>
    public virtual void Initialize(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        this.Url = url;
        foreach (var capture in Captures)
        {
            if (capture.Key.Item2)
            {
                if (capture.Key.Item1.Any(prefix => url.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    Capture = (Capture)Activator.CreateInstance(capture.Value, url)!;
                    Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {url}.");
                    break;
                }
            }
            else
            {
                if (capture.Key.Item1.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    Capture = (Capture)Activator.CreateInstance(capture.Value, url)!;
                    Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {url}.");
                    break;
                }
            }
        }

        if (Capture is null)
        {
            Capture = (Capture)Activator.CreateInstance(DefaultCapture, url)!;
            Logger.LogInformation($"Defaulting capture source to {DefaultCapture.Name} with url {url}.");
        }

        if (Capture is not null)
        {
            Capture.StartCapture();
            Logger.LogInformation($"Starting {DefaultCapture.Name} capture source...");
        }
    }

    /// <summary>
    /// Converts Capture.Frame into something Babble can understand
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public unsafe Task<bool> ExtractFrameData(Span<float> floatArray, Size size, CameraSettings settings)
    {
        if (Capture?.IsReady != true || Capture.RawMat == null || Capture.RawMat.DataPointer == null || Capture.FrameCount == _lastFrameCount)
            return Task.FromResult(false);
        if (floatArray.Length < size.Width * size.Height)
            throw new ArgumentException("Bad floatArray size");

        _lastFrameCount = Capture.FrameCount;

        fixed (float* array = floatArray)
        {
            using var finalMat = Mat<float>.FromPixelData(size.Height, size.Width, new IntPtr(array));
            settings.Brightness = 1.0f / 255.0f;
            return TransformRawImage(finalMat, settings);
        }
    }

    public unsafe Task<bool> TransformRawImage(Mat outputMat, CameraSettings settings)
    {
        // If this method is called from above, then the below checks don't apply
        // We need this in case we poll from Babble.Core.cs, in which the developer
        // Just wants the frame data, not expression data
        if (Capture?.IsReady != true || Capture.RawMat == null || Capture.RawMat.DataPointer == null)
            return Task.FromResult(false);

        var roiX = settings.RoiX;
        var roiY = settings.RoiY;
        var roiWidth = settings.RoiWidth;
        var roiHeight = settings.RoiHeight;
        var rotationRadians = settings.RotationRadians;
        var useRedChannel = settings.UseRedChannel;

        Mat sourceMat = Capture.RawMat, resultMat = new Mat(sourceMat, (roiX == 0 || roiY == 0 || roiWidth == 0 || roiHeight == 0 ||
            roiWidth == sourceMat.Width || roiHeight == sourceMat.Height) ? new Rect(0, 0, sourceMat.Width, sourceMat.Height) : new Rect(roiX, roiY, roiWidth, roiHeight));
        if (resultMat.Channels() >= 2)
        {
            var newMat = new Mat();
            if (useRedChannel)
                Cv2.ExtractChannel(resultMat, newMat, 0);
            else
                Cv2.CvtColor(resultMat, newMat, ColorConversionCodes.BGR2GRAY);
            resultMat.Dispose();
            resultMat = newMat;
        }
        if (resultMat.Type() != outputMat.Type() || settings.Brightness != 1)
        {
            var newMat = new Mat();
            resultMat.ConvertTo(newMat, outputMat.Type(), settings.Brightness);
            resultMat.Dispose();
            resultMat = newMat;
        }
        Size size = outputMat.Size();

        var hFlip = settings.UseHorizontalFlip;
        var vFlip = settings.UseVerticalFlip;

        if (rotationRadians != 0 || hFlip || vFlip)
        {
            double cos = Math.Cos(rotationRadians), sin = Math.Sin(rotationRadians);
            double scale = 1.0 / (Math.Abs(cos) + Math.Abs(sin));
            double hscale = (hFlip ? -1.0 : 1.0) * scale;
            double vscale = (vFlip ? -1.0 : 1.0) * scale;
            using var matrix = new Mat<double>(2, 3);
            Span<double> data = matrix.AsSpan<double>();
            data[0] = (double)size.Width / (double)resultMat.Width * cos * hscale;
            data[1] = (double)size.Height / (double)resultMat.Height * sin * hscale;
            data[2] = ((double)size.Width - ((double)size.Width * cos + (double)size.Height * sin) * hscale) * 0.5;
            data[3] = -(double)size.Width / (double)resultMat.Width * sin * vscale;
            data[4] = (double)size.Height / (double)resultMat.Height * cos * vscale;
            data[5] = ((double)size.Height + ((double)size.Width * sin - (double)size.Height * cos) * vscale) * 0.5;
            Cv2.WarpAffine(resultMat, outputMat, matrix, size);
        }
        else
        {
            try
            {
                Cv2.Resize(resultMat, outputMat, size);
            }
            catch (Exception)
            {
                resultMat.Dispose();
                return Task.FromResult(false);
            }
        }
        resultMat.Dispose();
        return Task.FromResult(true);
    }

    /// <summary>
    /// Shuts down the current Capture source
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public virtual void Terminate()
    {
        if (Capture is null)
        {
            throw new InvalidOperationException();
        }

        Logger.LogInformation("Stopping capture source...");
        Capture.StopCapture();
    }
}

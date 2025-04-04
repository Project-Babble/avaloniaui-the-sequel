﻿using System;
using System.Collections.Generic;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Services.Inference.Captures;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services.Inference.Platforms;

/// <summary>
/// Base class for camera capture and frame processing
/// </summary>
public class DesktopConnector : PlatformConnector
{
    private static readonly HashSet<string> SerialConnections
        = new(StringComparer.OrdinalIgnoreCase) { "com" };

    private static readonly HashSet<string> VftConnections
        = new(StringComparer.OrdinalIgnoreCase) { "/dev" };

    private static readonly HashSet<string> IpConnectionsPrefixes
        = new(StringComparer.OrdinalIgnoreCase) { "http", };

    private static readonly HashSet<string> IpConnectionsSuffixes
        = new(StringComparer.OrdinalIgnoreCase) { "local", "local/" };

    protected override Type DefaultCapture => typeof(OpenCvCapture);

    public DesktopConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger, settingsService)
    {
        Captures = new()
        {
            { (SerialConnections, false), typeof(SerialCameraCapture) },
            { (IpConnectionsPrefixes, false), typeof(IpCameraCapture) },
            { (IpConnectionsSuffixes, true), typeof(IpCameraCapture) }
        };

        if (OperatingSystem.IsLinux())
        {
            Captures.Add((VftConnections, false), typeof(VftCapture));
        }
    }
}

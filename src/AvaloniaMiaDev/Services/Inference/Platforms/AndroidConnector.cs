﻿using System;
using System.Collections.Generic;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Services.Inference.Captures;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services.Inference.Platforms;

/// <summary>
/// Special class for iOS, Android and UWP platforms where VideoCapture is not fully implemented
/// Support for MJPEG video streams only presently!
/// </summary>
public class AndroidConnector : PlatformConnector
{
    private static readonly HashSet<string> IpConnectionsPrefixes
        = new(StringComparer.OrdinalIgnoreCase) { "http", };

    private static readonly HashSet<string> IpConnectionsSuffixes
        = new(StringComparer.OrdinalIgnoreCase) { "local", "local/" };

    protected override Type DefaultCapture => typeof(IpCameraCapture);

    public AndroidConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger, settingsService)
    {
        Captures = new()
        {
            { (IpConnectionsPrefixes, false), typeof(IpCameraCapture) },
            { (IpConnectionsSuffixes, true), typeof(IpCameraCapture) }
        };
    }
}

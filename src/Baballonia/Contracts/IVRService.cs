﻿using System;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using Baballonia.Services;
using System.Threading.Tasks;
using Baballonia.Models;
using Baballonia.Services.Overlay;

namespace Baballonia.Contracts;

internal interface IVrService
{
    public Task<bool> StartOverlay(string[] arguments = null!, string[] blacklistedPrograms = null!, bool waitForExit = false);
    public Task<VrCalibrationStatus> GetStatusAsync();

    public Task<bool> StartCamerasAsync(VrCalibration calibration);

    public Task<bool> StartCalibrationAsync(VrCalibration calibration);

    public Task<bool> StartPreviewAsync(VrCalibration calibration);

    public Task<bool> StopPreviewAsync();
    public bool StopOverlay();
}

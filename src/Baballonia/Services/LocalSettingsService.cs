﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Microsoft.Extensions.Options;

namespace Baballonia.Services;

public class LocalSettingsService : ILocalSettingsService
{
    public const string DefaultApplicationDataFolder = "ApplicationData";
    public const string DefaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;

    private readonly string _localApplicationData = Utils.PersistentDataDirectory;
    private readonly string _applicationDataFolder;
    private readonly string _localSettingsFile;

    private IDictionary<string, object> _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options)
    {
        _fileService = fileService;
        _options = options.Value;

        _applicationDataFolder = Path.Combine(_localApplicationData, _options.ApplicationDataFolder ?? DefaultApplicationDataFolder);
        _localSettingsFile = _options.LocalSettingsFile ?? DefaultLocalSettingsFile;

        _settings = new Dictionary<string, object>();
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, _localSettingsFile)) ?? new Dictionary<string, object>();

            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false)
    {
        await InitializeAsync();

        if (_settings != null && _settings.TryGetValue(key, out var obj))
        {
            return await Json.ToObjectAsync<T>((string)obj);
        }

        return defaultValue;
    }

    public async Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
    {
        await InitializeAsync();

        _settings[key] = await Json.StringifyAsync(value!);

        await _fileService.Save(_applicationDataFolder, _localSettingsFile, _settings);
    }

    public async Task Load(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();
            var defaultValue = savedSettingAttribute.Default();

            var setting = await ReadSettingAsync(settingName, defaultValue, savedSettingAttribute.ForceLocal());
            object? convertedSetting;
            try
            {
                convertedSetting = Convert.ChangeType(setting, property.PropertyType);
            }
            catch
            {
                convertedSetting = defaultValue;
            }

            property.SetValue(instance, convertedSetting);
        }
    }

    public async Task Save(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();

            await SaveSettingAsync(settingName, property.GetValue(instance), savedSettingAttribute.ForceLocal());
        }
    }
}

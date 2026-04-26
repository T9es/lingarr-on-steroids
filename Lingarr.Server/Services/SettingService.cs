using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Listener;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services;

public delegate void SettingChangedHandler(SettingService ss, string setting);

public class SettingService : ISettingService
{
    private const string EncryptedCachePrefix = "__encrypted__:";

    private static readonly HashSet<string> SensitiveSettingKeys =
    [
        SettingKeys.Integration.RadarrApiKey,
        SettingKeys.Integration.SonarrApiKey,
        SettingKeys.Integration.RadarrInstances,
        SettingKeys.Integration.SonarrInstances,
        SettingKeys.Translation.OpenAi.ApiKey,
        SettingKeys.Translation.Anthropic.ApiKey,
        SettingKeys.Translation.LocalAi.ApiKey,
        SettingKeys.Translation.DeepL.DeeplApiKey,
        SettingKeys.Translation.Gemini.ApiKey,
        SettingKeys.Translation.DeepSeek.ApiKey,
        SettingKeys.Translation.Chutes.ApiKey,
        SettingKeys.Translation.NanoGpt.ApiKey,
        SettingKeys.Translation.LibreTranslate.ApiKey
    ];

    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<ISettingService> _logger;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _cacheOptions;

    public event SettingChangedHandler SettingChanged;

    public SettingService(
        LingarrDbContext dbContext,
        ILogger<ISettingService> logger,
        IMemoryCache memoryCache,
        SettingChangedListener settingChangedListener,
        IEncryptionService encryptionService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _encryptionService = encryptionService;
        _cache = memoryCache;

        _cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(1))
            .SetSlidingExpiration(TimeSpan.FromMinutes(30));

        SettingChanged += settingChangedListener.OnSettingChanged;
        SettingChanged += InvalidateCacheForSetting;
    }

    private void InvalidateCacheForSetting(SettingService ss, string setting)
    {
        _cache.Remove(setting);
        _cache.Remove(GetEncryptedCacheKey(setting));
    }

    public void OnSettingChange(string setting)
    {
        SettingChanged?.Invoke(this, setting);
    }

    /// <inheritdoc />
    public async Task<string?> GetSetting(string key)
    {
        if (_cache.TryGetValue(key, out string? cachedValue))
        {
            return cachedValue;
        }

        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
        var value = await ResolveSettingValueAsync(setting);

        if (value != null)
        {
            _cache.Set(key, value, _cacheOptions);
        }

        return value;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetSettings(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, string>();
        var keysList = keys.ToList();
        var keysToFetch = new List<string>();

        foreach (var key in keysList)
        {
            if (_cache.TryGetValue(key, out string? cachedValue))
            {
                if (cachedValue != null)
                {
                    result[key] = cachedValue;
                }
            }
            else
            {
                keysToFetch.Add(key);
            }
        }

        if (keysToFetch.Any())
        {
            var dbSettings = await _dbContext.Settings
                .Where(s => keysToFetch.Contains(s.Key))
                .ToListAsync();

            foreach (var setting in dbSettings)
            {
                var value = await ResolveSettingValueAsync(setting);
                result[setting.Key] = value ?? string.Empty;
                _cache.Set(setting.Key, value ?? string.Empty, _cacheOptions);
            }
        }

        foreach (var key in keysList)
        {
            if (!result.ContainsKey(key))
            {
                result[key] = string.Empty;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<List<T>> GetSettingAsJson<T>(string key) where T : class
    {
        var settingValue = await GetSetting(key);
        if (string.IsNullOrEmpty(settingValue))
        {
            return [];
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<T>>(
                settingValue,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize setting '{Key}'. Value: {Value}", key, settingValue);
            throw new JsonException($"Failed to deserialize setting '{key}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetSetting(string key, string value)
    {
        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
        var storedValue = PrepareStoredValue(key, value);

        if (setting == null)
        {
            setting = new Setting
            {
                Key = key,
                Value = storedValue
            };
            await _dbContext.Settings.AddAsync(setting);
        }
        else
        {
            setting.Value = storedValue;
        }

        await _dbContext.SaveChangesAsync();
        OnSettingChange(key);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetSettings(Dictionary<string, string> settings)
    {
        var keys = settings.Keys.ToList();
        var existingSettings = await _dbContext.Settings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s);

        foreach (var setting in settings)
        {
            var storedValue = PrepareStoredValue(setting.Key, setting.Value);
            if (existingSettings.TryGetValue(setting.Key, out var existingSetting))
            {
                existingSetting.Value = storedValue;
            }
            else
            {
                await _dbContext.Settings.AddAsync(new Setting
                {
                    Key = setting.Key,
                    Value = storedValue
                });
            }
        }

        await _dbContext.SaveChangesAsync();
        foreach (var setting in settings)
        {
            OnSettingChange(setting.Key);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetEncryptedSetting(string key, string value)
    {
        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
        var storedValue = _encryptionService.Encrypt(value);

        if (setting == null)
        {
            setting = new Setting
            {
                Key = key,
                Value = storedValue
            };
            await _dbContext.Settings.AddAsync(setting);
        }
        else
        {
            setting.Value = storedValue;
        }

        await _dbContext.SaveChangesAsync();
        OnSettingChange(key);
        return true;
    }

    /// <inheritdoc />
    public async Task<string?> GetEncryptedSetting(string key)
    {
        var cacheKey = GetEncryptedCacheKey(key);
        if (_cache.TryGetValue(cacheKey, out string? cachedValue))
        {
            return cachedValue;
        }

        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
        var value = await ResolveEncryptedSettingValueAsync(setting);

        if (value != null)
        {
            _cache.Set(cacheKey, value, _cacheOptions);
        }

        return value;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetEncryptedSettings(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, string>();
        var keysList = keys.ToList();
        var keysToFetch = new List<string>();

        foreach (var key in keysList)
        {
            var cacheKey = GetEncryptedCacheKey(key);
            if (_cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                if (cachedValue != null)
                {
                    result[key] = cachedValue;
                }
            }
            else
            {
                keysToFetch.Add(key);
            }
        }

        if (keysToFetch.Any())
        {
            var dbSettings = await _dbContext.Settings
                .Where(s => keysToFetch.Contains(s.Key))
                .ToListAsync();

            foreach (var setting in dbSettings)
            {
                var value = await ResolveEncryptedSettingValueAsync(setting);
                result[setting.Key] = value ?? string.Empty;
                _cache.Set(GetEncryptedCacheKey(setting.Key), value ?? string.Empty, _cacheOptions);
            }
        }

        foreach (var key in keysList)
        {
            if (!result.ContainsKey(key))
            {
                result[key] = string.Empty;
            }
        }

        return result;
    }

    private static bool IsSensitiveSetting(string key)
    {
        return SensitiveSettingKeys.Contains(key);
    }

    private static string GetEncryptedCacheKey(string key)
    {
        return $"{EncryptedCachePrefix}{key}";
    }

    private string PrepareStoredValue(string key, string value)
    {
        return IsSensitiveSetting(key) ? _encryptionService.Encrypt(value) : value;
    }

    private async Task<string?> ResolveSettingValueAsync(Setting? setting)
    {
        if (setting == null)
        {
            return null;
        }

        if (!IsSensitiveSetting(setting.Key) || string.IsNullOrEmpty(setting.Value))
        {
            return setting.Value;
        }

        var decryptedValue = _encryptionService.Decrypt(setting.Value);
        if (!string.Equals(decryptedValue, setting.Value, StringComparison.Ordinal))
        {
            return decryptedValue;
        }

        setting.Value = _encryptionService.Encrypt(setting.Value);
        await _dbContext.SaveChangesAsync();
        return decryptedValue;
    }

    private async Task<string?> ResolveEncryptedSettingValueAsync(Setting? setting)
    {
        if (setting == null || string.IsNullOrEmpty(setting.Value))
        {
            return setting?.Value;
        }

        var decryptedValue = _encryptionService.Decrypt(setting.Value);
        if (!string.Equals(decryptedValue, setting.Value, StringComparison.Ordinal))
        {
            return decryptedValue;
        }

        if (IsSensitiveSetting(setting.Key))
        {
            setting.Value = _encryptionService.Encrypt(setting.Value);
            await _dbContext.SaveChangesAsync();
        }

        return decryptedValue;
    }
}

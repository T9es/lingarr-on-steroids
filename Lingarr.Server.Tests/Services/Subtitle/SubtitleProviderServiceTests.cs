using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleProviderServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<SubtitleProviderService>> _loggerMock = new();
    private readonly Mock<ISettingService> _settingServiceMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly LingarrDbContext _dbContext;

    public SubtitleProviderServiceTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LingarrDbContext(options);

        _dbContext = new LingarrDbContext(options);

        // Setup ScopeFactory -> Scope -> ServiceProvider -> DbContext
        _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(LingarrDbContext))).Returns(_dbContext);
    }

    [Fact]
    public async Task SearchAndDownloadSubtitle_AllProvidersDailyLimitReached_ThrowsException()
    {
        // Arrange
        var providerMock = new Mock<ISubtitleProvider>();
        providerMock.Setup(x => x.Name).Returns("MockProvider");

        _serviceProviderMock.Setup(x => x.GetService(typeof(IEnumerable<ISubtitleProvider>)))
            .Returns(new List<ISubtitleProvider> { providerMock.Object });

        _settingServiceMock.Setup(x => x.GetSetting(SettingKeys.SubtitleProvider.DailyLimit))
            .ReturnsAsync("5");

        // Seed logs to hit limit
        for (int i = 0; i < 5; i++)
        {
            _dbContext.SubtitleProviderLogs.Add(new SubtitleProviderLog
            {
                ProviderName = "MockProvider",
                Message = "Download",
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync();

        var service = new SubtitleProviderService(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _settingServiceMock.Object,
            _scopeFactoryMock.Object);
            
        var mediaMock = new Mock<IMedia>();

        // Act & Assert
        await Assert.ThrowsAsync<SubtitleDownloadLimitException>(() => 
            service.SearchAndDownloadSubtitle(mediaMock.Object, MediaType.Movie, "en", CancellationToken.None));
    }
}

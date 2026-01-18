using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Sync;

public class ShowSync : IShowSync
{
    private readonly LingarrDbContext _dbContext;
    private readonly IImageSync _imageSync;

    public ShowSync(
        LingarrDbContext dbContext,
        IImageSync imageSync)
    {
        _dbContext = dbContext;
        _imageSync = imageSync;
    }

    /// <inheritdoc />
    public async Task<Show> SyncShow(SonarrShow sonarrShow, Show? existingShow = null)
    {
        var showEntity = existingShow;

        if (showEntity == null)
        {
            showEntity = new Show
            {
                SonarrId = sonarrShow.Id,
                Title = sonarrShow.Title,
                Path = sonarrShow.Path,
                DateAdded = !string.IsNullOrEmpty(sonarrShow.Added) ? DateTime.Parse(sonarrShow.Added).ToUniversalTime() : DateTime.UtcNow
            };
            _dbContext.Shows.Add(showEntity);
        }
        else
        {
            showEntity.Title = sonarrShow.Title;
            showEntity.Path = sonarrShow.Path;
            showEntity.DateAdded = !string.IsNullOrEmpty(sonarrShow.Added) ? DateTime.Parse(sonarrShow.Added).ToUniversalTime() : DateTime.UtcNow;
        }

        if (sonarrShow.Images?.Any() == true)
        {
            _imageSync.SyncImages(showEntity.Images, sonarrShow.Images);
        }

        return showEntity;
    }
}

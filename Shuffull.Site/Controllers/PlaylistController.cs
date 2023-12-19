﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shuffull.Site.Database;
using Shuffull.Site.Tools;
using Shuffull.Site.Models;
using System.Diagnostics;
using Shuffull.Site;
using Results = Shuffull.Shared.Networking.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shuffull.Site.Tools.Authorization;
using Shuffull.Shared.Networking.Models.Server;
using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace Shuffull.Tools.Controllers
{
    public class PlaylistController : Controller
    {
        private readonly IServiceProvider _services;

        public PlaylistController(IServiceProvider services)
        {
            _services = services;
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> Create(string name)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as Site.Database.Models.User;

            if (name.IsNullOrEmpty() || name.Length > 50)
            {
                return BadRequest("Name is not valid.");
            }

            var dbPlaylist = new Site.Database.Models.Playlist()
            {
                UserId = contextUser.UserId,
                Name = name,
                CurrentSongId = 0,
                PercentUntilReplayable = 0.9m,
                Version = DateTime.UtcNow
            };

            context.Playlists.Add(dbPlaylist);
            await context.SaveChangesAsync();

            return Ok(dbPlaylist);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddSong(long playlistId, long songId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as Site.Database.Models.User;
            var playlist = await context.Playlists
                .Where(x => x.UserId == contextUser.UserId && x.PlaylistId == playlistId)
                .FirstOrDefaultAsync();
            var song = await context.Songs
                .AsNoTracking()
                .Where(x => x.SongId == songId)
                .FirstOrDefaultAsync();
            var playlistSong = await context.PlaylistSongs
                .AsNoTracking()
                .Where(x => x.PlaylistId == playlistId && x.SongId == songId)
                .FirstOrDefaultAsync();

            if (playlist == null || song == null)
            {
                return NotFound();
            }
            else if (playlistSong != null)
            {
                return Ok("This song is already found on the playlist.");
            }

            playlistSong = new Site.Database.Models.PlaylistSong()
            {
                PlaylistId = playlistId,
                SongId = songId
            };

            context.PlaylistSongs.Add(playlistSong);
            playlist.Version = DateTime.UtcNow;
            await context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as Site.Database.Models.User;
            var playlists = await context.Playlists
                .AsNoTracking()
                .Where(x => x.UserId == contextUser.UserId)
                .ToListAsync();
            var result = ClassMapper.Mapper.Map<List<Playlist>>(playlists);

            return Ok(result);
        }

        // TODO: remove songs from playlist get and getall
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetList(long[] playlistIds)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var contextUser = HttpContext.Items["User"] as Site.Database.Models.User;
            var playlist = await context.Playlists
                .AsNoTracking()
                .Where(x => x.UserId == contextUser.UserId && playlistIds.Contains(x.PlaylistId))
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .ToListAsync();
            var result = ClassMapper.Mapper.Map<List<Playlist>>(playlist);

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);

            return Ok(resultStr);
        }

        [HttpGet]
        [Authorize]
        public string Get(long playlistId)
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlist = context.Playlists
                .AsNoTracking()
                .Where(x => playlistId == x.PlaylistId)
                .Include(x => x.PlaylistSongs)
                .ThenInclude(x => x.Song)
                .ToList();
            var resultList = ClassMapper.Mapper.Map<List<Playlist>>(playlist);
            var result = resultList[0];

            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            };
            var resultStr = JsonSerializer.Serialize(result, options);

            return resultStr;
        }
    }
}
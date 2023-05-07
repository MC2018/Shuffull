﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shuffull.Shared;

namespace Shuffull.Shared.Migrations
{
    [DbContext(typeof(ShuffullContext))]
    partial class ShuffullContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.32");

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.Playlist", b =>
                {
                    b.Property<long>("PlaylistId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("CurrentSongId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<decimal>("PercentUntilReplayable")
                        .HasColumnType("TEXT");

                    b.Property<long>("UserId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("VersionCounter")
                        .HasColumnType("INTEGER");

                    b.HasKey("PlaylistId");

                    b.ToTable("Playlists");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.PlaylistSong", b =>
                {
                    b.Property<long>("PlaylistSongId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("InQueue")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("LastAddedToQueue")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("LastPlayed")
                        .HasColumnType("TEXT");

                    b.Property<long>("PlaylistId")
                        .HasColumnType("INTEGER");

                    b.Property<long>("SongId")
                        .HasColumnType("INTEGER");

                    b.HasKey("PlaylistSongId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("SongId");

                    b.ToTable("PlaylistSongs");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.RecentlyPlayedSong", b =>
                {
                    b.Property<string>("RecentlyPlayedSongGuid")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("LastPlayed")
                        .HasColumnType("TEXT");

                    b.Property<long>("SongId")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("TimestampSeconds")
                        .HasColumnType("INTEGER");

                    b.HasKey("RecentlyPlayedSongGuid");

                    b.HasIndex("SongId");

                    b.HasIndex("TimestampSeconds");

                    b.ToTable("RecentlyPlayedSongs");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.Requests.Request", b =>
                {
                    b.Property<string>("Guid")
                        .HasColumnType("TEXT");

                    b.Property<string>("RequestName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("RequestType")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("TimeRequested")
                        .HasColumnType("TEXT");

                    b.HasKey("Guid");

                    b.ToTable("Requests");

                    b.HasDiscriminator<string>("RequestName").HasValue("Request");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.Song", b =>
                {
                    b.Property<long>("SongId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Directory")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("SongId");

                    b.ToTable("Songs");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.Requests.UpdatePlaylistsRequest", b =>
                {
                    b.HasBaseType("Shuffull.Shared.Networking.Models.Requests.Request");

                    b.HasDiscriminator().HasValue("UpdatePlaylists");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.Requests.UpdateSongLastPlayedRequest", b =>
                {
                    b.HasBaseType("Shuffull.Shared.Networking.Models.Requests.Request");

                    b.Property<DateTime>("LastPlayed")
                        .HasColumnType("TEXT");

                    b.Property<long>("SongId")
                        .HasColumnType("INTEGER");

                    b.HasDiscriminator().HasValue("UpdateSongLastPlayed");
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.PlaylistSong", b =>
                {
                    b.HasOne("Shuffull.Shared.Networking.Models.Playlist", "Playlist")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("PlaylistId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Shared.Networking.Models.Song", "Song")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Shuffull.Shared.Networking.Models.RecentlyPlayedSong", b =>
                {
                    b.HasOne("Shuffull.Shared.Networking.Models.Song", "Song")
                        .WithMany()
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}

﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shuffull.Site;

#nullable disable

namespace Shuffull.Site.Migrations
{
    [DbContext(typeof(ShuffullContext))]
    partial class ShuffullContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.3")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Shuffull.Site.Models.Database.Artist", b =>
                {
                    b.Property<string>("ArtistId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("ArtistId");

                    b.HasIndex("Name");

                    b.ToTable("Artists");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Playlist", b =>
                {
                    b.Property<string>("PlaylistId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("CurrentSongId")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PercentUntilReplayable")
                        .HasColumnType("decimal(2,2)");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTime>("Version")
                        .HasColumnType("datetime2");

                    b.HasKey("PlaylistId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("UserId");

                    b.ToTable("Playlists");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.PlaylistSong", b =>
                {
                    b.Property<string>("PlaylistSongId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("PlaylistId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("SongId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("PlaylistSongId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("SongId");

                    b.ToTable("PlaylistSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Song", b =>
                {
                    b.Property<string>("SongId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("FileExtension")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("FileHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("SongId");

                    b.HasIndex("Name");

                    b.ToTable("Songs");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.SongArtist", b =>
                {
                    b.Property<string>("SongArtistId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("ArtistId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("SongId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("SongArtistId");

                    b.HasIndex("ArtistId");

                    b.HasIndex("SongId");

                    b.ToTable("SongArtists");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.SongTag", b =>
                {
                    b.Property<string>("SongTagId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("SongId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("TagId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("SongTagId");

                    b.HasIndex("SongId");

                    b.HasIndex("TagId");

                    b.ToTable("SongTags");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Tag", b =>
                {
                    b.Property<string>("TagId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.HasKey("TagId");

                    b.ToTable("Tags");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.User", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("ServerHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTime>("Version")
                        .HasColumnType("datetime2");

                    b.HasKey("UserId");

                    b.HasIndex("Username");

                    b.HasIndex("Version");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.UserSong", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("SongId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTime>("LastPlayed")
                        .HasColumnType("datetime2");

                    b.Property<DateTime>("Version")
                        .HasColumnType("datetime2");

                    b.HasKey("UserId", "SongId");

                    b.HasIndex("LastPlayed");

                    b.HasIndex("SongId");

                    b.HasIndex("UserId");

                    b.ToTable("UserSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Playlist", b =>
                {
                    b.HasOne("Shuffull.Site.Models.Database.User", "User")
                        .WithMany("Playlists")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.PlaylistSong", b =>
                {
                    b.HasOne("Shuffull.Site.Models.Database.Playlist", "Playlist")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("PlaylistId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Models.Database.Song", "Song")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Playlist");

                    b.Navigation("Song");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.SongArtist", b =>
                {
                    b.HasOne("Shuffull.Site.Models.Database.Artist", "Artist")
                        .WithMany("SongArtists")
                        .HasForeignKey("ArtistId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Models.Database.Song", "Song")
                        .WithMany("SongArtists")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Artist");

                    b.Navigation("Song");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.SongTag", b =>
                {
                    b.HasOne("Shuffull.Site.Models.Database.Song", "Song")
                        .WithMany("SongTags")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Models.Database.Tag", "Tag")
                        .WithMany("SongTags")
                        .HasForeignKey("TagId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Song");

                    b.Navigation("Tag");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.UserSong", b =>
                {
                    b.HasOne("Shuffull.Site.Models.Database.Song", "Song")
                        .WithMany("UserSongs")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Models.Database.User", "User")
                        .WithMany("UserSongs")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Song");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Artist", b =>
                {
                    b.Navigation("SongArtists");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Playlist", b =>
                {
                    b.Navigation("PlaylistSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Song", b =>
                {
                    b.Navigation("PlaylistSongs");

                    b.Navigation("SongArtists");

                    b.Navigation("SongTags");

                    b.Navigation("UserSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.Tag", b =>
                {
                    b.Navigation("SongTags");
                });

            modelBuilder.Entity("Shuffull.Site.Models.Database.User", b =>
                {
                    b.Navigation("Playlists");

                    b.Navigation("UserSongs");
                });
#pragma warning restore 612, 618
        }
    }
}

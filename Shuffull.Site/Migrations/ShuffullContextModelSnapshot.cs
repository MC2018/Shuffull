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

            modelBuilder.Entity("Shuffull.Site.Database.Models.Artist", b =>
                {
                    b.Property<long>("ArtistId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("ArtistId"));

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("ArtistId");

                    b.HasIndex("Name");

                    b.ToTable("Artists");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Playlist", b =>
                {
                    b.Property<long>("PlaylistId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("PlaylistId"));

                    b.Property<long>("CurrentSongId")
                        .HasColumnType("bigint");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PercentUntilReplayable")
                        .HasColumnType("decimal(2,2)");

                    b.Property<long>("UserId")
                        .HasColumnType("bigint");

                    b.Property<DateTime>("Version")
                        .HasColumnType("datetime2");

                    b.HasKey("PlaylistId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("UserId");

                    b.ToTable("Playlists");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.PlaylistSong", b =>
                {
                    b.Property<long>("PlaylistSongId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("PlaylistSongId"));

                    b.Property<long>("PlaylistId")
                        .HasColumnType("bigint");

                    b.Property<long>("SongId")
                        .HasColumnType("bigint");

                    b.HasKey("PlaylistSongId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("SongId");

                    b.ToTable("PlaylistSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Song", b =>
                {
                    b.Property<long>("SongId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("SongId"));

                    b.Property<string>("Directory")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("SongId");

                    b.HasIndex("Name");

                    b.ToTable("Songs");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.SongArtist", b =>
                {
                    b.Property<long>("SongArtistId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("SongArtistId"));

                    b.Property<long>("ArtistId")
                        .HasColumnType("bigint");

                    b.Property<long>("SongId")
                        .HasColumnType("bigint");

                    b.HasKey("SongArtistId");

                    b.HasIndex("ArtistId");

                    b.HasIndex("SongId");

                    b.ToTable("SongArtists");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.SongTag", b =>
                {
                    b.Property<long>("SongTagId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("SongTagId"));

                    b.Property<long>("SongId")
                        .HasColumnType("bigint");

                    b.Property<long>("TagId")
                        .HasColumnType("bigint");

                    b.HasKey("SongTagId");

                    b.HasIndex("SongId");

                    b.HasIndex("TagId");

                    b.ToTable("SongTags");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Tag", b =>
                {
                    b.Property<long>("TagId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("TagId"));

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.HasKey("TagId");

                    b.ToTable("Tags");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.User", b =>
                {
                    b.Property<long>("UserId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("UserId"));

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

            modelBuilder.Entity("Shuffull.Site.Database.Models.UserSong", b =>
                {
                    b.Property<long>("UserId")
                        .HasColumnType("bigint");

                    b.Property<long>("SongId")
                        .HasColumnType("bigint");

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

            modelBuilder.Entity("Shuffull.Site.Database.Models.Playlist", b =>
                {
                    b.HasOne("Shuffull.Site.Database.Models.User", "User")
                        .WithMany("Playlists")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.PlaylistSong", b =>
                {
                    b.HasOne("Shuffull.Site.Database.Models.Playlist", "Playlist")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("PlaylistId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Database.Models.Song", "Song")
                        .WithMany("PlaylistSongs")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Playlist");

                    b.Navigation("Song");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.SongArtist", b =>
                {
                    b.HasOne("Shuffull.Site.Database.Models.Artist", "Artist")
                        .WithMany("SongArtists")
                        .HasForeignKey("ArtistId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Database.Models.Song", "Song")
                        .WithMany("SongArtists")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Artist");

                    b.Navigation("Song");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.SongTag", b =>
                {
                    b.HasOne("Shuffull.Site.Database.Models.Song", "Song")
                        .WithMany("SongTags")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Database.Models.Tag", "Tag")
                        .WithMany("SongTags")
                        .HasForeignKey("TagId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Song");

                    b.Navigation("Tag");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.UserSong", b =>
                {
                    b.HasOne("Shuffull.Site.Database.Models.Song", "Song")
                        .WithMany("UserSongs")
                        .HasForeignKey("SongId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Shuffull.Site.Database.Models.User", "User")
                        .WithMany("UserSongs")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Song");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Artist", b =>
                {
                    b.Navigation("SongArtists");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Playlist", b =>
                {
                    b.Navigation("PlaylistSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Song", b =>
                {
                    b.Navigation("PlaylistSongs");

                    b.Navigation("SongArtists");

                    b.Navigation("SongTags");

                    b.Navigation("UserSongs");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.Tag", b =>
                {
                    b.Navigation("SongTags");
                });

            modelBuilder.Entity("Shuffull.Site.Database.Models.User", b =>
                {
                    b.Navigation("Playlists");

                    b.Navigation("UserSongs");
                });
#pragma warning restore 612, 618
        }
    }
}

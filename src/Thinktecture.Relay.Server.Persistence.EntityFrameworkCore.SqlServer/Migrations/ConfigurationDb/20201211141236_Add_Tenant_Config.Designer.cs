﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Thinktecture.Relay.Server.Persistence.EntityFrameworkCore;

namespace Thinktecture.Relay.Server.Persistence.EntityFrameworkCore.SqlServer.Migrations.ConfigurationDb
{
    [DbContext(typeof(RelayDbContext))]
    [Migration("20201211141236_Add_Tenant_Config")]
    partial class Add_Tenant_Config
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.7")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.ClientSecret", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTime>("Created")
                        .HasColumnType("datetime2");

                    b.Property<DateTime?>("Expiration")
                        .HasColumnType("datetime2");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("nvarchar(4000)")
                        .HasMaxLength(4000);

                    b.HasKey("Id");

                    b.HasIndex("TenantId");

                    b.ToTable("ClientSecrets");
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Config", b =>
                {
                    b.Property<Guid>("TenantId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool?>("EnableTracing")
                        .HasColumnType("bit");

                    b.Property<TimeSpan?>("KeepAliveInterval")
                        .HasColumnType("time");

                    b.Property<TimeSpan?>("ReconnectMaximumDelay")
                        .HasColumnType("time");

                    b.Property<TimeSpan?>("ReconnectMinimumDelay")
                        .HasColumnType("time");

                    b.HasKey("TenantId");

                    b.ToTable("Configs");
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Connection", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(100)")
                        .HasMaxLength(100);

                    b.Property<DateTimeOffset>("ConnectTime")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("DisconnectTime")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("LastActivityTime")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("OriginId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RemoteIpAddress")
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("OriginId");

                    b.HasIndex("TenantId");

                    b.ToTable("Connections");
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Origin", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("LastSeenTime")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset?>("ShutdownTime")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTimeOffset>("StartupTime")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("Origins");
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Tenant", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Description")
                        .HasColumnType("nvarchar(1000)")
                        .HasMaxLength(1000);

                    b.Property<string>("DisplayName")
                        .HasColumnType("nvarchar(200)")
                        .HasMaxLength(200);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(100)")
                        .HasMaxLength(100);

                    b.Property<string>("NormalizedName")
                        .IsRequired()
                        .HasColumnType("nvarchar(100)")
                        .HasMaxLength(100);

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.HasIndex("NormalizedName")
                        .IsUnique();

                    b.ToTable("Tenants");
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.ClientSecret", b =>
                {
                    b.HasOne("Thinktecture.Relay.Server.Persistence.Models.Tenant", null)
                        .WithMany("ClientSecrets")
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Config", b =>
                {
                    b.HasOne("Thinktecture.Relay.Server.Persistence.Models.Tenant", null)
                        .WithOne("Config")
                        .HasForeignKey("Thinktecture.Relay.Server.Persistence.Models.Config", "TenantId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Thinktecture.Relay.Server.Persistence.Models.Connection", b =>
                {
                    b.HasOne("Thinktecture.Relay.Server.Persistence.Models.Origin", null)
                        .WithMany("Connections")
                        .HasForeignKey("OriginId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Thinktecture.Relay.Server.Persistence.Models.Tenant", null)
                        .WithMany("Connections")
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}

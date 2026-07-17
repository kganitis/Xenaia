using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Xenaia.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "availabilities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_product_id = table.Column<int>(type: "integer", nullable: false),
                    external_option_id = table.Column<int>(type: "integer", nullable: false),
                    timeslot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vacancies = table.Column<int>(type: "integer", nullable: true),
                    stop_sales = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availabilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secret_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    referrer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    channel_booking_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    activity_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    lead_contact_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_external = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_external = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "extras",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extras", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "participant_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_option_id = table.Column<int>(type: "integer", nullable: false),
                    alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_participant_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_status = table.Column<int>(type: "integer", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "booking_extra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    external_option_id = table.Column<int>(type: "integer", nullable: false),
                    extra_alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    booking_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_extra", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_extra_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_gift_card",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    booking_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_gift_card", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_gift_card_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    external_product_id = table.Column<int>(type: "integer", nullable: false),
                    external_option_id = table.Column<int>(type: "integer", nullable: false),
                    participant_type_alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    checked_in = table.Column<bool>(type: "boolean", nullable: false),
                    booking_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_item_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_payment",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    booking_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_payment", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_payment_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_option",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_option", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_option_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_option_extra",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    extra_id = table.Column<int>(type: "integer", nullable: false),
                    product_option_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_option_extra", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_option_extra_product_option_product_option_id",
                        column: x => x.product_option_id,
                        principalTable: "product_option",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_availabilities_external_product_id_external_option_id_times",
                table: "availabilities",
                columns: new[] { "external_product_id", "external_option_id", "timeslot_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_extra_booking_id_external_id",
                table: "booking_extra",
                columns: new[] { "booking_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_gift_card_booking_id_code",
                table: "booking_gift_card",
                columns: new[] { "booking_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_item_booking_id_external_id",
                table: "booking_item",
                columns: new[] { "booking_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_booking_payment_booking_id_external_id",
                table: "booking_payment",
                columns: new[] { "booking_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_code",
                table: "bookings",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_external_id",
                table: "categories",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channels_code",
                table: "channels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_extras_alias",
                table: "extras",
                column: "alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox",
                column: "occurred_at",
                filter: "processed_at IS NULL AND error IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_participant_types_product_option_id_alias",
                table: "participant_types",
                columns: new[] { "product_option_id", "alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_types_code",
                table: "payment_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_option_product_id_external_id",
                table: "product_option",
                columns: new[] { "product_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_option_extra_product_option_id_extra_id",
                table: "product_option_extra",
                columns: new[] { "product_option_id", "extra_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_code",
                table: "products",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_external_id",
                table: "products",
                column: "external_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availabilities");

            migrationBuilder.DropTable(
                name: "booking_extra");

            migrationBuilder.DropTable(
                name: "booking_gift_card");

            migrationBuilder.DropTable(
                name: "booking_item");

            migrationBuilder.DropTable(
                name: "booking_payment");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "extras");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "participant_types");

            migrationBuilder.DropTable(
                name: "payment_types");

            migrationBuilder.DropTable(
                name: "product_option_extra");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "product_option");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}

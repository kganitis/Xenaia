using Xenaia.Core;
using Xenaia.Data;
using Xenaia.Data.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddXenaiaCore(builder.Configuration);
builder.Services.AddXenaiaPostgreSql(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<XenaiaDbContext>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();

using ProjectMahjong.Contracts;
using ProjectMahjong.Game;
using ProjectMahjong.Server.Hubs;
using ProjectMahjong.Server.Rooms;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.Configure<FuzhouRuleOptions>(builder.Configuration.GetSection("FuzhouRules"));
builder.Services.AddSingleton<RoomRegistry>();
builder.Services.AddHostedService<RoomCleanupService>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 32 * 1024;
    options.StatefulReconnectBufferSize = 32 * 1024;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}
else
{
    app.UseHttpsRedirection();
}
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/rooms", async (CreateRoomRequest request, RoomRegistry rooms) =>
{
    var validationError = ValidateDisplayName(request.DisplayName);
    return validationError is null
        ? Results.Ok(await rooms.CreateRoomAsync(request.DisplayName.Trim()))
        : Results.BadRequest(new ApiErrorDto(validationError));
});

app.MapPost("/api/rooms/{roomCode}/join", async (
    string roomCode,
    JoinRoomRequest request,
    RoomRegistry rooms) =>
{
    var validationError = ValidateDisplayName(request.DisplayName);
    if (validationError is not null)
    {
        return Results.BadRequest(new ApiErrorDto(validationError));
    }

    var (response, error) = await rooms.JoinRoomAsync(roomCode, request.DisplayName.Trim());
    return response is not null
        ? Results.Ok(response)
        : Results.BadRequest(new ApiErrorDto(error ?? "加入房间失败。"));
});

app.MapPost("/api/rooms/{roomCode}/rejoin", async (
    string roomCode,
    RejoinRoomRequest request,
    RoomRegistry rooms) =>
{
    var (response, error) = await rooms.RejoinRoomAsync(
        roomCode,
        request.PlayerId,
        request.ReconnectToken);
    return response is not null
        ? Results.Ok(response)
        : Results.BadRequest(new ApiErrorDto(error ?? "恢复座位失败。"));
});

app.MapHub<GameHub>(GameHubRoutes.Path, options =>
{
    options.AllowStatefulReconnects = true;
});

app.MapFallbackToFile("index.html");
app.Run();

static string? ValidateDisplayName(string? displayName)
{
    var trimmed = displayName?.Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
    {
        return "请输入名字。";
    }

    return trimmed.Length > 12 ? "名字最多十二个字符。" : null;
}

public partial class Program;

using System.Text.Json;
using Microsoft.JSInterop;
using ProjectMahjong.Contracts;

namespace ProjectMahjong.Client.Services;

public sealed class SessionStorage(IJSRuntime jsRuntime)
{
    private const string LastRoomKey = "fuzhou-mahjong:last-room";

    public async Task SaveAsync(PlayerSessionDto session)
    {
        var json = JsonSerializer.Serialize(session);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", RoomKey(session.RoomCode), json);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", LastRoomKey, session.RoomCode);
    }

    public async Task<PlayerSessionDto?> LoadAsync(string roomCode)
    {
        var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", RoomKey(roomCode));
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PlayerSessionDto>(json);
        }
        catch (JsonException)
        {
            await RemoveAsync(roomCode);
            return null;
        }
    }

    public async Task<PlayerSessionDto?> LoadLastAsync()
    {
        var roomCode = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", LastRoomKey);
        return string.IsNullOrWhiteSpace(roomCode) ? null : await LoadAsync(roomCode);
    }

    public async Task RemoveAsync(string roomCode)
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", RoomKey(roomCode));
    }

    private static string RoomKey(string roomCode) =>
        $"fuzhou-mahjong:session:{roomCode.Trim().ToUpperInvariant()}";
}


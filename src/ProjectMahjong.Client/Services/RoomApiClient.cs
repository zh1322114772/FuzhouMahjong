using System.Net.Http.Json;
using ProjectMahjong.Contracts;

namespace ProjectMahjong.Client.Services;

public sealed class RoomApiClient(HttpClient httpClient)
{
    public Task<ApiCallResult<SessionResponseDto>> CreateRoomAsync(string displayName) =>
        SendAsync(HttpMethod.Post, "/api/rooms", new CreateRoomRequest(displayName));

    public Task<ApiCallResult<SessionResponseDto>> JoinRoomAsync(string roomCode, string displayName) =>
        SendAsync(
            HttpMethod.Post,
            $"/api/rooms/{Uri.EscapeDataString(roomCode.Trim())}/join",
            new JoinRoomRequest(displayName));

    public Task<ApiCallResult<SessionResponseDto>> RejoinRoomAsync(PlayerSessionDto session) =>
        SendAsync(
            HttpMethod.Post,
            $"/api/rooms/{Uri.EscapeDataString(session.RoomCode)}/rejoin",
            new RejoinRoomRequest(session.PlayerId, session.ReconnectToken));

    private async Task<ApiCallResult<SessionResponseDto>> SendAsync<TRequest>(
        HttpMethod method,
        string uri,
        TRequest request)
    {
        try
        {
            using var message = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(request)
            };
            using var response = await httpClient.SendAsync(message);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<SessionResponseDto>();
                return value is null
                    ? ApiCallResult<SessionResponseDto>.Failure("服务器返回了空响应。")
                    : ApiCallResult<SessionResponseDto>.Success(value);
            }

            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            return ApiCallResult<SessionResponseDto>.Failure(error?.Error ?? "请求失败，请稍后再试。");
        }
        catch (HttpRequestException)
        {
            return ApiCallResult<SessionResponseDto>.Failure("无法连接服务器，请检查网络。 ");
        }
    }
}


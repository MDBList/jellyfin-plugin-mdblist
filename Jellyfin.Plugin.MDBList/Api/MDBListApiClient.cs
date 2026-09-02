using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MDBList.Api.Models;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.MDBList.Api;

/// <summary>
/// MDBList API client -- port of the Kodi addon's mdblist_api.py. Every
/// method takes an already-resolved access token rather than looking one up
/// itself, keeping this a pure HTTP-wire-format concern (token resolution
/// per linked Jellyfin user lives in <see cref="OAuthService"/>).
///
/// Carries over the hardening rule from the Kodi addon's own bug fixes: a
/// non-2xx response, an unparseable body, or a response missing a field
/// callers depend on all throw <see cref="MDBListApiException"/> rather
/// than being coerced into an empty/default value -- diff-based sync reads
/// "no data" as authoritative, so a real failure must abort the run instead
/// of looking like "there is nothing to sync".
/// </summary>
public class MDBListApiClient
{
    private const string BaseUrl = "https://api.mdblist.com";

    // Response models expose their collections as get-only (Collection<T>/
    // Dictionary<TKey,TValue>) to satisfy CA2227/CA1002 -- but
    // System.Text.Json does NOT populate a get-only collection property by
    // default (unlike XmlSerializer's population-via-getter behavior).
    // Populate opts into that: confirmed empirically, since this is easy to
    // silently get wrong and have every page deserialize as "empty" instead
    // of throwing -- which a diff-based sync would read as "nothing to
    // sync" rather than a real bug.
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    public MDBListApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Fetches /sync/last_activities -- the cheap per-bucket watermark check.
    /// </summary>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed activity buckets.</returns>
    public async Task<LastActivities> FetchLastActivitiesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var data = await RequestAsync<LastActivities>(accessToken, HttpMethod.Get, "/sync/last_activities", null, null, cancellationToken)
            .ConfigureAwait(false);

        if (data is null || string.IsNullOrEmpty(data.ServerTime))
        {
            throw new MDBListApiException("Malformed response from /sync/last_activities");
        }

        return data;
    }

    /// <summary>
    /// Cursor-paginates a /sync/watched or /sync/ratings GET endpoint and
    /// merges every page's movies/episodes.
    /// </summary>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="endpoint">The endpoint path, e.g. "/sync/watched".</param>
    /// <param name="mediatype">Optional media type filter.</param>
    /// <param name="since">Optional "since" timestamp filter.</param>
    /// <param name="extended">
    /// "ids_only" for the lossy fast mode (movies only expose a tmdb id), or
    /// null for full mode (every provider id) -- full mode is required
    /// whenever local items need to be matched or ruled out by any id other
    /// than tmdb.
    /// </param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged movie/episode rows across every page.</returns>
    public async Task<SyncItemsResult> FetchSyncItemsAsync(
        string accessToken,
        string endpoint,
        string? mediatype,
        string? since,
        string? extended,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = new SyncItemsResult();
        string? cursor = null;

        do
        {
            var query = new Dictionary<string, string?>
            {
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
                ["extended"] = extended,
                ["mediatype"] = mediatype,
                ["since"] = since,
                ["cursor"] = cursor,
            };

            var page = await RequestAsync<SyncItemsPage>(accessToken, HttpMethod.Get, endpoint, query, null, cancellationToken)
                .ConfigureAwait(false);
            if (page is null)
            {
                break;
            }

            foreach (var movie in page.Movies)
            {
                result.Movies.Add(movie);
            }

            foreach (var episode in page.Episodes)
            {
                result.Episodes.Add(episode);
            }

            cursor = page.Pagination?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>
    /// Cursor-paginates /sync/journal starting from <paramref name="since"/>.
    /// </summary>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="since">The caller's last synced-at cursor.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The merged journal rows, or <see cref="JournalResult.RequiresFullSync"/>
    /// set if <paramref name="since"/> is outside the 30-day retention window.
    /// </returns>
    public async Task<JournalResult> FetchJournalAsync(string accessToken, string? since, int limit, CancellationToken cancellationToken)
    {
        var result = new JournalResult();
        string? cursor = null;

        do
        {
            var query = new Dictionary<string, string?> { ["limit"] = limit.ToString(CultureInfo.InvariantCulture) };
            if (!string.IsNullOrEmpty(cursor))
            {
                query["cursor"] = cursor;
            }
            else if (!string.IsNullOrEmpty(since))
            {
                query["since"] = since;
            }

            var page = await RequestAsync<JournalPage>(accessToken, HttpMethod.Get, "/sync/journal", query, null, cancellationToken)
                .ConfigureAwait(false);
            if (page is null)
            {
                break;
            }

            if (page.RequiresFullSync)
            {
                result.RequiresFullSync = true;
                result.Entries.Clear();
                return result;
            }

            foreach (var entry in page.Journal)
            {
                result.Entries.Add(entry);
            }

            result.JournalOldestAt = page.JournalOldestAt ?? result.JournalOldestAt;
            cursor = page.Pagination?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>
    /// Pushes a sync payload (add or remove) to a /sync/* POST endpoint.
    /// </summary>
    /// <param name="accessToken">A valid MDBList access token.</param>
    /// <param name="endpoint">The endpoint path, e.g. "/sync/watched" or "/sync/watched/remove".</param>
    /// <param name="payload">The payload to serialize as the request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PushSyncItemsAsync(string accessToken, string endpoint, object payload, CancellationToken cancellationToken)
    {
        await RequestRawAsync(accessToken, HttpMethod.Post, endpoint, null, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> RequestAsync<T>(
        string accessToken,
        HttpMethod method,
        string endpoint,
        IDictionary<string, string?>? query,
        object? jsonBody,
        CancellationToken cancellationToken)
    {
        var body = await RequestRawAsync(accessToken, method, endpoint, query, jsonBody, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<T>(body, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            throw new MDBListApiException($"Invalid response from {endpoint}", ex);
        }
    }

    private async Task<string> RequestRawAsync(
        string accessToken,
        HttpMethod method,
        string endpoint,
        IDictionary<string, string?>? query,
        object? jsonBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new MDBListApiException("Not authenticated");
        }

        var url = BaseUrl + endpoint;
        if (query is { Count: > 0 })
        {
            var queryString = string.Join(
                '&',
                query
                    .Where(pair => !string.IsNullOrEmpty(pair.Value))
                    .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
            if (!string.IsNullOrEmpty(queryString))
            {
                url += "?" + queryString;
            }
        }

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MDBListApiException(ex.Message, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var truncated = text.Length <= 200 ? text : text[..200];
                throw new MDBListApiException($"API Error {(int)response.StatusCode}: {truncated}");
            }

            return text;
        }
    }
}

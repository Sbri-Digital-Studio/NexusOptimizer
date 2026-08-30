using System.Net.Http;
using System.Text;
using NexusOptimizer.Core.Configuration;
using NexusOptimizer.Core.Logging;
using NexusOptimizer.Core.Notifications;
using NexusOptimizer.Core.Updates;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Controllo aggiornamenti: l'unica funzione dell'applicazione che effettua una
/// chiamata di rete, e solo se la persona la attiva e configura un canale HTTPS.
///
/// Cosa fa: scarica un piccolo manifest JSON e confronta la versione. Cosa NON fa:
/// non invia alcun dato (nessun identificativo, nessuna statistica), non scarica
/// binari e non installa nulla. Con un aggiornamento disponibile mostra un avviso
/// con il collegamento alla pagina della release, che si apre nel browser.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly NotificationCenter _center;
    private readonly FileLogService _log;
    private HttpClient? _client;

    public UpdateService(AppConfig config, ConfigStore store, NotificationCenter center, FileLogService log)
    {
        _config = config;
        _store = store;
        _center = center;
        _log = log;
    }

    /// <summary>Versione in esecuzione, mostrata anche accanto all'esito del controllo.</summary>
    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    /// <summary>
    /// Controllo automatico all'avvio, al massimo una volta al giorno. Se il
    /// controllo e' disattivato o il canale non e' configurato non parte alcuna
    /// richiesta: il programma resta completamente offline.
    /// </summary>
    public async Task<UpdateCheckResult> CheckIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.CheckForUpdates) return new UpdateCheckResult(UpdateCheckStatus.Disabled);
        if (!UpdateChannel.IsSupportedFeed(_config.UpdateFeedUrl))
            return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);
        if (_config.LastUpdateCheckUtc is DateTime last
            && DateTime.UtcNow - last < UpdateChannel.AutomaticCheckInterval)
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, CurrentVersionText);

        return await CheckAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Controllo su richiesta esplicita (pulsante "Controlla ora").</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.CheckForUpdates) return new UpdateCheckResult(UpdateCheckStatus.Disabled);
        var feed = _config.UpdateFeedUrl;
        if (!UpdateChannel.IsSupportedFeed(feed)) return new UpdateCheckResult(UpdateCheckStatus.NotConfigured);

        try
        {
            var json = await DownloadManifestAsync(feed, cancellationToken).ConfigureAwait(false);
            var manifest = UpdateChannel.Parse(json);
            if (manifest is null)
            {
                _log.Warning("Manifest aggiornamenti non valido: nessuna versione annunciata.");
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            _config.LastUpdateCheckUtc = DateTime.UtcNow;
            Persist();

            if (!UpdateChannel.IsNewer(manifest.Version, CurrentVersion))
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, CurrentVersionText);

            AnnounceIfNotAlreadySeen(manifest);
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, manifest.Version,
                string.IsNullOrWhiteSpace(manifest.Url) ? null : manifest.Url);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (Exception ex)
        {
            // Un canale irraggiungibile non e' un errore dell'utente: si dichiara
            // il fallimento senza allarmi e senza ritentare da soli.
            _log.Error("Controllo aggiornamenti non riuscito", ex);
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    private async Task<string> DownloadManifestAsync(string feed, CancellationToken cancellationToken)
    {
        var client = _client ??= CreateClient();
        using var response = await client
            .GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Tetto di lettura: un manifest legittimo sta in pochi kilobyte e non deve
        // poter riempire la memoria del processo.
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[UpdateChannel.MaxManifestBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await stream
                .ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)
                .ConfigureAwait(false);
            if (chunk == 0) break;
            read += chunk;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            MaxAutomaticRedirections = 3,
        };
        var client = new HttpClient(handler) { Timeout = RequestTimeout };
        // Nessun identificativo della macchina: solo prodotto e versione, come
        // qualunque client HTTP e' tenuto a dichiarare.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusOptimizer/" + CurrentVersionText);
        return client;
    }

    /// <summary>
    /// La stessa versione viene annunciata una volta sola: chi decide di restare
    /// sulla build corrente non deve rivedere l'avviso a ogni avvio.
    /// </summary>
    private void AnnounceIfNotAlreadySeen(UpdateManifest manifest)
    {
        if (string.Equals(_config.LastSeenUpdateVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
            return;

        _config.LastSeenUpdateVersion = manifest.Version;
        Persist();
        _center.Publish(new NotificationRecord
        {
            Key = "update.available:" + manifest.Version,
            TitleKey = "notif.update.title",
            MessageKey = "notif.update.msg",
            MessageArgs = [manifest.Version, CurrentVersionText],
            Severity = NotificationSeverity.Info,
            TargetUrl = string.IsNullOrWhiteSpace(manifest.Url) ? null : manifest.Url,
        });
    }

    private void Persist()
    {
        try { _store.Save(_config); }
        catch (Exception ex) { _log.Error("Salvataggio esito controllo aggiornamenti non riuscito", ex); }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}

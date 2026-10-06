namespace HAMMOR.App.Presence;

/// <summary>
/// A live loudness envelope the Living Core can follow: HAMMOR's speech
/// while Speaking, later the user's voice while Listening.
/// </summary>
/// <remarks>
/// <para>
/// This is the input seam for real audio. P0 connects no source: the platform
/// player does not expose levels yet, so Speaking runs its designed rhythm
/// and Listening shows no voice rings. A later source (for example NAudio
/// metering in the platform layer, or a microphone once speech-to-text
/// exists) implements this interface and is set on
/// <see cref="LivingCore.VoiceEnvelope"/>; the visual states do not change.
/// </para>
/// <para>
/// Never fake this. An implementation must report real audio or report
/// nothing.
/// </para>
/// </remarks>
public interface ISpeechEnvelope
{
    /// <summary>
    /// Current level, 0 (silent) to 1 (loudest), read once per frame on the UI
    /// thread. Must be cheap and must not block.
    /// </summary>
    /// <returns>False when no real level is available right now.</returns>
    bool TryGetLevel(out double level);
}

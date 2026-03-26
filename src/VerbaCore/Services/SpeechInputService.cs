using System.Speech.Recognition;

namespace VerbaCore.Services;

public sealed class SpeechInputService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _isListening;

    public event EventHandler<string>? SpeechRecognized;
    public event EventHandler<bool>? ListeningStateChanged;

    public bool IsListening => _isListening;

    public void StartListening(string culture = "en-US")
    {
        if (_isListening) return;

        try
        {
            // Dispose previous engine if any (e.g. after StopListening)
            _engine?.Dispose();
            _engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(culture));
            _engine.LoadGrammar(new DictationGrammar());
            _engine.SetInputToDefaultAudioDevice();

            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.RecognizeCompleted += OnRecognizeCompleted;

            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _isListening = true;
            ListeningStateChanged?.Invoke(this, true);
        }
        catch (Exception)
        {
            _isListening = false;
            ListeningStateChanged?.Invoke(this, false);
        }
    }

    public void StopListening()
    {
        if (!_isListening || _engine == null) return;

        _engine.RecognizeAsyncCancel();
        _isListening = false;
        ListeningStateChanged?.Invoke(this, false);
    }

    public void ToggleListening(string culture = "en-US")
    {
        if (_isListening)
            StopListening();
        else
            StartListening(culture);
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence > 0.5f)
        {
            SpeechRecognized?.Invoke(this, e.Result.Text);
        }
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        _isListening = false;
        ListeningStateChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        StopListening();
        _engine?.Dispose();
    }
}

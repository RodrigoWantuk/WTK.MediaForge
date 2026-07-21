using System.Security.Cryptography;
using System.Text;

namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingProtocol
{
    private readonly Dictionary<RemoteScenePeerRole, RoleSequenceState> _sequenceByRole = [];
    private bool _offerActive;
    private bool _answered;
    private bool _renegotiationRequested;

    public bool Accept(RemoteScenePeerRole role, RemoteSceneSignalingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateShape(message);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{message.Kind}\0{message.Payload}")));
        if (!_sequenceByRole.TryGetValue(role, out var sequenceState))
            _sequenceByRole[role] = sequenceState = new RoleSequenceState();
        if (sequenceState.Fingerprints.TryGetValue(message.Sequence, out var seenFingerprint))
        {
            if (string.Equals(fingerprint, seenFingerprint, StringComparison.Ordinal))
                return false;
            throw new InvalidDataException("Signaling sequence was reused with different content.");
        }
        if (message.Sequence < sequenceState.LastSequence)
            throw new InvalidDataException("Signaling sequence regressed.");

        switch (message.Kind)
        {
            case RemoteSceneSignalingMessageKind.Offer:
                RequireRole(role, RemoteScenePeerRole.Publisher, message.Kind);
                if (_offerActive && !_renegotiationRequested)
                    throw new InvalidDataException("A new Offer requires an explicit Renegotiate message.");
                _offerActive = true;
                _answered = false;
                _renegotiationRequested = false;
                break;
            case RemoteSceneSignalingMessageKind.Answer:
                RequireRole(role, RemoteScenePeerRole.Subscriber, message.Kind);
                if (!_offerActive || _answered)
                    throw new InvalidDataException("Answer requires one pending Offer.");
                _answered = true;
                break;
            case RemoteSceneSignalingMessageKind.IceCandidate:
                if (!_offerActive)
                    throw new InvalidDataException("ICE candidate requires an active negotiation.");
                break;
            case RemoteSceneSignalingMessageKind.KeyFrameRequest:
                RequireRole(role, RemoteScenePeerRole.Subscriber, message.Kind);
                if (!_answered)
                    throw new InvalidDataException("Keyframe feedback requires an established negotiation.");
                break;
            case RemoteSceneSignalingMessageKind.Renegotiate:
                RequireRole(role, RemoteScenePeerRole.Publisher, message.Kind);
                if (!_answered)
                    throw new InvalidDataException("Renegotiation requires an established negotiation.");
                _renegotiationRequested = true;
                break;
        }

        sequenceState.LastSequence = message.Sequence;
        sequenceState.Fingerprints.Add(message.Sequence, fingerprint);
        if (sequenceState.Fingerprints.Count > 256)
            sequenceState.Fingerprints.Remove(sequenceState.Fingerprints.Keys.Min());
        return true;
    }

    private static void ValidateShape(RemoteSceneSignalingMessage message)
    {
        if (!Enum.IsDefined(message.Kind))
            throw new InvalidDataException($"Unsupported signaling message kind '{message.Kind}'.");
        if (string.IsNullOrWhiteSpace(message.Payload))
            throw new InvalidDataException("Signaling message payload is required.");
        if (message.Sequence < 0)
            throw new InvalidDataException("Signaling message sequence cannot be negative.");
    }

    private static void RequireRole(RemoteScenePeerRole actual, RemoteScenePeerRole expected, RemoteSceneSignalingMessageKind kind)
    {
        if (actual != expected)
            throw new InvalidDataException($"{kind} is not valid for the {actual} role.");
    }

    private sealed class RoleSequenceState
    {
        public long LastSequence { get; set; } = -1;
        public Dictionary<long, string> Fingerprints { get; } = [];
    }
}

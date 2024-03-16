﻿using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ManagedBass;
using ManagedBass.Mix;
using UnityEngine;
using YARG.Core.Audio;
using YARG.Core.Logging;

namespace YARG.Audio.BASS
{
    public sealed class BassStemChannel : StemChannel
    {
        private readonly int _sourceHandle;

        private StreamHandle _streamHandles;
        private StreamHandle _reverbHandles;
        private PitchShiftParametersStruct _pitchParams;

        private bool _isReverbing;
        private double _lastStemVolume;

        internal BassStemChannel(AudioManager manager, SongStem stem, int sourceStream, double volume, in PitchShiftParametersStruct pitchParams, in StreamHandle streamHandles, in StreamHandle reverbHandles)
            : base(manager, stem, volume)
        {
            _sourceHandle = sourceStream;
            _streamHandles = streamHandles;
            _reverbHandles = reverbHandles;
            _lastStemVolume = volume;
            _pitchParams = pitchParams;
        }

        public override void SetWhammyPitch(float percent)
        {
            if (_streamHandles.PitchFX == 0 || _reverbHandles.PitchFX == 0)
                return;

            percent = Mathf.Clamp(percent, 0f, 1f);

            float shift = Mathf.Pow(2, -(AudioManager.WhammyPitchShiftAmount * percent) / 12);
            _pitchParams.fPitchShift = shift;

            if (!BassHelpers.FXSetParameters(_streamHandles.PitchFX, _pitchParams))
            {
                YargLogger.LogFormatError("Failed to set params (normal fx): {0}", Bass.LastError);
            }

            if (!BassHelpers.FXSetParameters(_reverbHandles.PitchFX, _pitchParams))
            {
                YargLogger.LogFormatError("Failed to set params (reverb fx): {0}", Bass.LastError);
            }
        }

        public override void SetPosition(double position, bool bufferCompensation = true)
        {
            // Playback buffer compensation is optional
            // All other desync compensation is always done
            position += bufferCompensation ? _manager.PlaybackBufferLength : 0;

            // Hack to get desync of pitch-bent channels
            if (_streamHandles.PitchFX != 0 && _reverbHandles.PitchFX != 0)
            {
                // The desync is caused by the FFT window
                // BASS_FX does not account for it automatically so we must do it ourselves
                // (thanks Matt/Oscar for the info!)
                if (Bass.ChannelGetAttribute(_streamHandles.Stream, ChannelAttribute.Frequency, out float sampleRate))
                    position += _pitchParams.FFTSize / sampleRate;
                else
                    YargLogger.LogFormatError("Failed to get sample rate: {0}!", Bass.LastError);
            }

            long bytes = Bass.ChannelSeconds2Bytes(_streamHandles.Stream, position);
            if (bytes < 0)
            {
                YargLogger.LogFormatError("Failed to get byte position at {0}!", position);
                return;
            }

            bool success = BassMix.ChannelSetPosition(_streamHandles.Stream, bytes, PositionFlags.Bytes);
            if (!success)
            {
                YargLogger.LogFormatError("Failed to seek to position {0}!", position);
            }
        }

        public override void SetSpeed(float speed)
        {
            BassAudioManager.SetSpeed(speed, _streamHandles.Stream, _reverbHandles.Stream);
        }

        protected override void SetVolume(double newVolume)
        {
            double volumeSetting = AudioManager.GetVolumeSetting(Stem);

            double oldBassVol = _lastStemVolume * Volume;
            double newBassVol = volumeSetting * newVolume;

            // Limit minimum stem volume
            if (AudioManager.UseMinimumStemVolume)
            {
                newBassVol = Math.Max(newBassVol, AudioManager.MINIMUM_STEM_VOLUME);
            }

            // Values are the same, no need to change
            if (Math.Abs(oldBassVol - newBassVol) < double.Epsilon)
            {
                return;
            }

            _volume = newVolume;
            _lastStemVolume = volumeSetting;

            if (!Bass.ChannelSetAttribute(_streamHandles.Stream, ChannelAttribute.Volume, newBassVol))
                YargLogger.LogFormatError("Failed to set stream volume: {0}!", Bass.LastError);

            double reverbVolume = _isReverbing ? newBassVol * BassHelpers.REVERB_VOLUME_MULTIPLIER : 0;

            if (!Bass.ChannelSetAttribute(_reverbHandles.Stream, ChannelAttribute.Volume, reverbVolume))
                YargLogger.LogFormatError("Failed to set reverb volume: {0}!", Bass.LastError);
        }

        protected override void SetReverb(bool reverb)
        {
            _isReverbing = reverb;
            if (reverb)
            {
                // Reverb already applied
                if (_reverbHandles.ReverbFX != 0) return;

                // Set reverb FX
                _reverbHandles.LowEQ = BassHelpers.AddEqToChannel(_reverbHandles.Stream, BassHelpers.LowEqParams);
                _reverbHandles.MidEQ = BassHelpers.AddEqToChannel(_reverbHandles.Stream, BassHelpers.MidEqParams);
                _reverbHandles.HighEQ = BassHelpers.AddEqToChannel(_reverbHandles.Stream, BassHelpers.HighEqParams);
                _reverbHandles.ReverbFX = BassHelpers.AddReverbToChannel(_reverbHandles.Stream);

                double volumeSetting = AudioManager.GetVolumeSetting(Stem);
                if (!Bass.ChannelSlideAttribute(_reverbHandles.Stream, ChannelAttribute.Volume,
                    (float) (volumeSetting * Volume * BassHelpers.REVERB_VOLUME_MULTIPLIER), BassHelpers.REVERB_SLIDE_IN_MILLISECONDS))
                {
                    YargLogger.LogFormatError("Failed to set reverb volume: {0}!", Bass.LastError);
                }
            }
            else
            {
                // No reverb is applied
                if (_reverbHandles.ReverbFX == 0) return;

                // Remove low-high
                if (!Bass.ChannelRemoveFX(_reverbHandles.Stream, _reverbHandles.LowEQ) ||
                    !Bass.ChannelRemoveFX(_reverbHandles.Stream, _reverbHandles.MidEQ) ||
                    !Bass.ChannelRemoveFX(_reverbHandles.Stream, _reverbHandles.HighEQ) ||
                    !Bass.ChannelRemoveFX(_reverbHandles.Stream, _reverbHandles.ReverbFX))
                {
                    YargLogger.LogFormatError("Failed to remove effects: {0}!", Bass.LastError);
                }

                _reverbHandles.LowEQ = 0;
                _reverbHandles.MidEQ = 0;
                _reverbHandles.HighEQ = 0;
                _reverbHandles.ReverbFX = 0;

                if (!Bass.ChannelSlideAttribute(_reverbHandles.Stream, ChannelAttribute.Volume, 0,
                    BassHelpers.REVERB_SLIDE_OUT_MILLISECONDS))
                {
                    YargLogger.LogFormatError("Failed to set reverb volume: {0}!", Bass.LastError);
                }
            }
        }

        protected override void DisposeUnmanagedResources()
        {
            _streamHandles.Dispose();
            _reverbHandles.Dispose();

            if (_sourceHandle != 0)
            {
                if (!Bass.StreamFree(_sourceHandle) && Bass.LastError != Errors.Handle)
                    YargLogger.LogFormatError("Failed to free file stream (THIS WILL LEAK MEMORY): {0}!", Bass.LastError);
            }
        }
    }
}
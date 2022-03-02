// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>Configures the decoding of Slice stream parameters and return values.</summary>
    public sealed class SliceStreamDecoderOptions
    {
        /// <summary>The default instance.</summary>
        public static SliceStreamDecoderOptions Default { get; } = new();

        /// <summary>When the Slice engine decodes a stream into an async enumerable, it will pause when the number of
        /// bytes decoded but not read is greater or equal to this value.</summary>
        /// <value>A value greater than 0 is the threshold in bytes; 0 means no threshold.</value>
        public long PauseWriterThreshold { get; }

        /// <summary>When the decoding of a stream into an async enumerable is paused
        /// (<see cref="PauseWriterThreshold"/>), the decoding resumes when the number of bytes decoded but not read yet
        /// falls below this threshold.</summary>
        public long ResumeWriterThreshold { get; }

        /// <summary>Constructs a new Slice stream decoder feature.</summary>
        /// <param name="pauseWriterThreshold">The pause writer threshold value. -1 means use the default value. 0 means
        /// never pause.</param>
        /// <param name="resumeWriterThreshold">The resume writer threshold value. -1 means use half of
        /// <paramref name="pauseWriterThreshold"/>.</param>
        public SliceStreamDecoderOptions(long pauseWriterThreshold = -1, long resumeWriterThreshold = -1)
        {
            const int DefaultPauseWriterThreshold = 65_536; // 64K, like System.IO.Pipelines.Pipe

            if (pauseWriterThreshold == -1)
            {
                pauseWriterThreshold = DefaultPauseWriterThreshold;
            }
            else if (pauseWriterThreshold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pauseWriterThreshold),
                    $"{nameof(pauseWriterThreshold)} must be -1 or positive");
            }
            PauseWriterThreshold = pauseWriterThreshold;

            if (resumeWriterThreshold == -1)
            {
                resumeWriterThreshold = pauseWriterThreshold / 2;
            }
            else if (resumeWriterThreshold < 0 || resumeWriterThreshold > pauseWriterThreshold)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resumeWriterThreshold),
                    $"{nameof(resumeWriterThreshold)} must be -1 or between 0 and {nameof(pauseWriterThreshold)}");
            }
            ResumeWriterThreshold = resumeWriterThreshold;
        }
    }
}

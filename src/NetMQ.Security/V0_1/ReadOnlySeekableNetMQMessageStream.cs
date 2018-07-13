using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetMQ.Security.V0_1
{
    internal static class NetMqMessageStreamExtension
    {
        internal static Stream ToStream(this NetMQMessage message)
        {
            return new ReadOnlySeekableNetMQMessageStream(message);
        }
    }

    public class ReadOnlySeekableNetMQMessageStream : Stream
    {
        private readonly NetMQMessage _message;
        private long _position;
        private int _currentFrame;
        private int _currentFramePosition;

        public ReadOnlySeekableNetMQMessageStream(NetMQMessage message)
        {
            _message = message;
            _position = 0;
            _currentFrame = 0;
            _currentFramePosition = 0;
        }
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                int len = 0;
                for (int i = 0; i < _message.FrameCount; i++)
                {
                    len += _message[i].MessageSize;
                }
                return len;
            }
        }

        public byte? CurrentByte
        {
            get
            {
                if (_message.FrameCount > _currentFrame && _message[_currentFrame].MessageSize > _currentFramePosition)
                    return _message[_currentFrame].Buffer[_currentFramePosition];
                else
                    return null;
            }
        }
        public override long Position
        {
            get => _position;
            set
            {
                if (value == _position) return;

                if (value < 0 || value > Length) throw new ArgumentOutOfRangeException("value/Position");

                int travel = (int)(value - _position);
                if (travel > 0) // going forwards
                {
                    // are we moving forward less than the size of the current frame's array length less the current position in that array?
                    if (_message[_currentFrame].MessageSize - _currentFramePosition > travel)
                    {
                        _currentFramePosition += travel;
                    }
                    // if not, update the current frame indexer and position
                    else
                    {
                        int counter = 0;
                        while (_currentFrame < _message.FrameCount && counter < travel)
                        {
                            counter += _message[_currentFrame++].MessageSize - _currentFramePosition;
                            _currentFramePosition = 0;
                        }

                        // did the amount of travel land us _exactly_ at the start of the current frame?
                        if (counter > travel)
                        {
                            _currentFramePosition = counter - travel;
                        }
                    }
                }
                else if (travel < 0) // going backwards
                {
                    // are we moving backward less than the current frame position?
                    if (_currentFramePosition > travel)
                    {
                        _currentFramePosition -= travel;
                    }
                    // if not, update the current indexer and position
                    else
                    {
                        int counter = 0;
                        while (_currentFrame >= 0 && counter > travel)
                        {
                            counter -= _message[_currentFrame--].MessageSize + _currentFramePosition;
                            _currentFramePosition = _message[_currentFrame].MessageSize;
                        }

                        if (counter < travel)
                        {
                            _currentFramePosition = travel - counter;
                        }
                    }
                }


                _position = value;
                return;
            }
        }

        public override void Flush()
        {
            return;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (count < 0) throw new ArgumentOutOfRangeException("count");

            if (offset != 0)
            {
                Position = Position + offset;
            }

            int bytesRead = 0;

            while (_currentFrame < _message.FrameCount && bytesRead < count)
            {
                byte[] currFrameBytes = _message[_currentFrame].Buffer;
                // how many bytes could we possibly read from this buffer?
                int bytesAvailableToReadInCurrentFrame = currFrameBytes.Length - _currentFramePosition;

                // are there enough bytes left in this buffer to satisfy the whole request?
                if (count < bytesAvailableToReadInCurrentFrame)
                {
                    int bytesToRead = count - bytesRead;
                    Buffer.BlockCopy(currFrameBytes, _currentFramePosition, buffer, bytesRead, bytesToRead);
                    _currentFramePosition += bytesToRead;
                    bytesRead += bytesToRead;
                }
                // nope, so we'll also have to increase the frame tracking numbers
                else
                {
                    Buffer.BlockCopy(currFrameBytes, _currentFramePosition, buffer, bytesRead, bytesAvailableToReadInCurrentFrame);
                    _currentFramePosition = 0;
                    _currentFrame++;
                    bytesRead += bytesAvailableToReadInCurrentFrame;
                }
            }

            if (bytesRead != 0)
            {
                // don't use the property here - we've already updated the current frame position properly
                _position += bytesRead;
            }
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
                default:
                    break;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

}

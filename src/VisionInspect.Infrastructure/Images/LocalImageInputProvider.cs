using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Images
{
    /// <summary>
    /// 本地图片输入源。支持手动、顺序、循环三种播放方式。
    /// 图片只在获取时读取，降低内存占用；文件名作为源标识，便于日志追踪。
    /// </summary>
    public sealed class LocalImageInputProvider : IInputProvider
    {
        private static readonly string[] SupportedExtensions =
        {
            ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
        };

        private readonly object _sync = new object();
        private readonly List<string> _images = new List<string>();
        private int _currentIndex;
        private PlaybackMode _playbackMode = PlaybackMode.Loop;

        public string Name
        {
            get
            {
                lock (_sync)
                    return _images.Count == 0 ? "本地图片（空）" : string.Format("本地图片（{0} 张）", _images.Count);
            }
        }

        public PlaybackMode PlaybackMode
        {
            get { lock (_sync) return _playbackMode; }
            set { lock (_sync) _playbackMode = value; }
        }

        public int Count
        {
            get { lock (_sync) return _images.Count; }
        }

        public int CurrentIndex
        {
            get { lock (_sync) return _currentIndex; }
        }

        public string CurrentPath
        {
            get
            {
                lock (_sync)
                    return _images.Count == 0 ? null : _images[Math.Min(_currentIndex, _images.Count - 1)];
            }
        }

        public IReadOnlyList<string> ImagePaths
        {
            get { lock (_sync) return _images.ToList(); }
        }

        public void SetImages(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");

            var valid = paths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFullPath)
                .Where(File.Exists)
                .Where(x => SupportedExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_sync)
            {
                _images.Clear();
                _images.AddRange(valid);
                _currentIndex = 0;
            }
        }

        public void AddImages(IEnumerable<string> paths)
        {
            if (paths == null) return;
            var current = ImagePaths.ToList();
            current.AddRange(paths);
            SetImages(current);
        }

        public void Clear()
        {
            lock (_sync)
            {
                _images.Clear();
                _currentIndex = 0;
            }
        }

        public void SetCurrentIndex(int index)
        {
            lock (_sync)
            {
                if (_images.Count == 0)
                {
                    _currentIndex = 0;
                    return;
                }
                _currentIndex = Math.Max(0, Math.Min(index, _images.Count - 1));
            }
        }

        public void ResetPosition()
        {
            lock (_sync) _currentIndex = 0;
        }

        public Task<RawImage> AcquireAsync(CancellationToken cancellationToken)
        {
            string path;
            lock (_sync)
            {
                if (_images.Count == 0)
                    throw new VisionInspectException("本地图片列表为空，请先导入图片。");

                if (_currentIndex >= _images.Count)
                {
                    if (_playbackMode == PlaybackMode.Loop) _currentIndex = 0;
                    else throw new EndOfSequenceException();
                }

                path = _images[_currentIndex];
                if (_playbackMode != PlaybackMode.Manual)
                {
                    _currentIndex++;
                    if (_currentIndex >= _images.Count && _playbackMode == PlaybackMode.Loop)
                        _currentIndex = 0;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return RawImage.FromFile(path, Path.GetFileName(path));
            }, cancellationToken);
        }
    }
}

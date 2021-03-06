// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Reflection.Metadata;

#if SRM
using System.Reflection.Internal;
using BitArithmeticUtilities = System.Reflection.Internal.BitArithmetic;
#else
using System;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis.Collections;
using Roslyn.Utilities;
#endif

#if SRM
namespace System.Reflection.Metadata.Ecma335
#else
namespace Roslyn.Reflection.Metadata.Ecma335
#endif
{
#if SRM && FUTURE
    public
#endif
    sealed partial class MetadataBuilder
    {
        // #US heap
        private readonly Dictionary<string, int> _userStrings = new Dictionary<string, int>();
        private readonly BlobBuilder _userStringWriter = new BlobBuilder(1024);
        private readonly int _userStringHeapStartOffset;

        // #String heap
        private Dictionary<string, StringHandle> _strings = new Dictionary<string, StringHandle>(128);
        private int[] _stringIndexToResolvedOffsetMap;
        private BlobBuilder _stringWriter;
        private readonly int _stringHeapStartOffset;

        // #Blob heap
        private readonly Dictionary<ImmutableArray<byte>, BlobHandle> _blobs = new Dictionary<ImmutableArray<byte>, BlobHandle>(ByteSequenceComparer.Instance);
        private readonly int _blobHeapStartOffset;
        private int _blobHeapSize;

        // #GUID heap
        private readonly Dictionary<Guid, GuidHandle> _guids = new Dictionary<Guid, GuidHandle>();
        private readonly BlobBuilder _guidWriter = new BlobBuilder(16); // full metadata has just a single guid

        private bool _streamsAreComplete;

        public MetadataBuilder(
            int userStringHeapStartOffset = 0,
            int stringHeapStartOffset = 0,
            int blobHeapStartOffset = 0,
            int guidHeapStartOffset = 0)
        {
            // Add zero-th entry to all heaps, even in EnC delta.
            // We don't want generation-relative handles to ever be IsNil.
            // In both full and delta metadata all nil heap handles should have zero value.
            // There should be no blob handle that references the 0 byte added at the 
            // beginning of the delta blob.
            _userStringWriter.WriteByte(0);

            _blobs.Add(ImmutableArray<byte>.Empty, default(BlobHandle));
            _blobHeapSize = 1;

            // When EnC delta is applied #US, #String and #Blob heaps are appended.
            // Thus indices of strings and blobs added to this generation are offset
            // by the sum of respective heap sizes of all previous generations.
            _userStringHeapStartOffset = userStringHeapStartOffset;
            _stringHeapStartOffset = stringHeapStartOffset;
            _blobHeapStartOffset = blobHeapStartOffset;

            // Unlike other heaps, #Guid heap in EnC delta is zero-padded.
            _guidWriter.WriteBytes(0, guidHeapStartOffset);
        }

        public BlobHandle GetBlob(BlobBuilder builder)
        {
            // TODO: avoid making a copy if the blob exists in the index
            return GetBlob(builder.ToImmutableArray());
        }

        public BlobHandle GetBlob(ImmutableArray<byte> blob)
        {
            BlobHandle index;
            if (!_blobs.TryGetValue(blob, out index))
            {
                Debug.Assert(!_streamsAreComplete);

                index = MetadataTokens.BlobHandle(_blobHeapSize);
                _blobs.Add(blob, index);

                _blobHeapSize += BlobWriterImpl.GetCompressedIntegerSize(blob.Length) + blob.Length;
            }
            
            return index;
        }

        public BlobHandle GetConstantBlob(object value)
        {
            string str = value as string;
            if (str != null)
            {
                return this.GetBlob(str);
            }

            var writer = new BlobBuilder();
            writer.WriteConstant(value);
            return this.GetBlob(writer);
        }

        public BlobHandle GetBlob(string str)
        {
            byte[] byteArray = new byte[str.Length * 2];
            int i = 0;
            foreach (char ch in str)
            {
                byteArray[i++] = (byte)(ch & 0xFF);
                byteArray[i++] = (byte)(ch >> 8);
            }

            return this.GetBlob(ImmutableArray.Create(byteArray));
        }

        public BlobHandle GetBlobUtf8(string str)
        {
            return GetBlob(ImmutableArray.Create(Encoding.UTF8.GetBytes(str)));
        }

        public GuidHandle GetGuid(Guid guid)
        {
            if (guid == Guid.Empty)
            {
                return default(GuidHandle);
            }

            GuidHandle result;
            if (_guids.TryGetValue(guid, out result))
            {
                return result;
            }

            result = GetNextGuid();
            _guids.Add(guid, result);
            _guidWriter.WriteBytes(guid.ToByteArray());
            return result;
        }

        public GuidHandle ReserveGuid(out Blob reservedBlob)
        {
            var handle = GetNextGuid();
            reservedBlob = _guidWriter.ReserveBytes(16);
            return handle;
        }

        private GuidHandle GetNextGuid()
        {
            Debug.Assert(!_streamsAreComplete);

            // The only GUIDs that are serialized are MVID, EncId, and EncBaseId in the
            // Module table. Each of those GUID offsets are relative to the local heap,
            // even for deltas, so there's no need for a GetGuidStreamPosition() method
            // to offset the positions by the size of the original heap in delta metadata.
            // Unlike #Blob, #String and #US streams delta #GUID stream is padded to the 
            // size of the previous generation #GUID stream before new GUIDs are added.

            // Metadata Spec: 
            // The Guid heap is an array of GUIDs, each 16 bytes wide. 
            // Its first element is numbered 1, its second 2, and so on.
            return MetadataTokens.GuidHandle((_guidWriter.Count >> 4) + 1);
        }

        public StringHandle GetString(string str)
        {
            StringHandle index;
            if (str.Length == 0)
            {
                index = default(StringHandle);
            }
            else if (!_strings.TryGetValue(str, out index))
            {
                Debug.Assert(!_streamsAreComplete);
                index = MetadataTokens.StringHandle(_strings.Count + 1); // idx 0 is reserved for empty string
                _strings.Add(str, index);
            }

            return index;
        }

        public int GetHeapOffset(StringHandle handle)
        {
            return _stringIndexToResolvedOffsetMap[MetadataTokens.GetHeapOffset(handle)];
        }

        public int GetHeapOffset(BlobHandle handle)
        {
            int offset = MetadataTokens.GetHeapOffset(handle);
            return (offset == 0) ? 0 : _blobHeapStartOffset + offset;
        }

        public int GetHeapOffset(GuidHandle handle)
        {
            return MetadataTokens.GetHeapOffset(handle);
        }

        public int GetHeapOffset(UserStringHandle handle)
        {
            return MetadataTokens.GetHeapOffset(handle);
        }

        public UserStringHandle GetUserString(string str)
        {
            int index;
            if (!_userStrings.TryGetValue(str, out index))
            {
                Debug.Assert(!_streamsAreComplete);

                index = _userStringWriter.Position + _userStringHeapStartOffset;
                _userStrings.Add(str, index);
                _userStringWriter.WriteCompressedInteger(str.Length * 2 + 1);

                _userStringWriter.WriteUTF16(str);

                // Write out a trailing byte indicating if the string is really quite simple
                byte stringKind = 0;
                foreach (char ch in str)
                {
                    if (ch >= 0x7F)
                    {
                        stringKind = 1;
                    }
                    else
                    {
                        switch ((int)ch)
                        {
                            case 0x1:
                            case 0x2:
                            case 0x3:
                            case 0x4:
                            case 0x5:
                            case 0x6:
                            case 0x7:
                            case 0x8:
                            case 0xE:
                            case 0xF:
                            case 0x10:
                            case 0x11:
                            case 0x12:
                            case 0x13:
                            case 0x14:
                            case 0x15:
                            case 0x16:
                            case 0x17:
                            case 0x18:
                            case 0x19:
                            case 0x1A:
                            case 0x1B:
                            case 0x1C:
                            case 0x1D:
                            case 0x1E:
                            case 0x1F:
                            case 0x27:
                            case 0x2D:
                                stringKind = 1;
                                break;
                            default:
                                continue;
                        }
                    }

                    break;
                }

                _userStringWriter.WriteByte(stringKind);
            }

            return MetadataTokens.UserStringHandle(index);
        }

        internal void CompleteHeaps()
        {
            Debug.Assert(!_streamsAreComplete);
            _streamsAreComplete = true;
            SerializeStringHeap();
        }

        public ImmutableArray<int> GetHeapSizes()
        {
            var heapSizes = new int[MetadataTokens.HeapCount];

            heapSizes[(int)HeapIndex.UserString] = _userStringWriter.Count;
            heapSizes[(int)HeapIndex.String] = _stringWriter.Count;
            heapSizes[(int)HeapIndex.Blob] = _blobHeapSize;
            heapSizes[(int)HeapIndex.Guid] = _guidWriter.Count;

            return ImmutableArray.CreateRange(heapSizes);
        }

        /// <summary>
        /// Fills in stringIndexMap with data from stringIndex and write to stringWriter.
        /// Releases stringIndex as the stringTable is sealed after this point.
        /// </summary>
        private void SerializeStringHeap()
        {
            // Sort by suffix and remove stringIndex
            var sorted = new List<KeyValuePair<string, StringHandle>>(_strings);
            sorted.Sort(new SuffixSort());
            _strings = null;

            _stringWriter = new BlobBuilder(1024);

            // Create VirtIdx to Idx map and add entry for empty string
            _stringIndexToResolvedOffsetMap = new int[sorted.Count + 1];

            _stringIndexToResolvedOffsetMap[0] = 0;
            _stringWriter.WriteByte(0);

            // Find strings that can be folded
            string prev = string.Empty;
            foreach (KeyValuePair<string, StringHandle> entry in sorted)
            {
                int position = _stringHeapStartOffset + _stringWriter.Position;
                
                // It is important to use ordinal comparison otherwise we'll use the current culture!
                if (prev.EndsWith(entry.Key, StringComparison.Ordinal) && !BlobUtilities.IsLowSurrogateChar(entry.Key[0]))
                {
                    // Map over the tail of prev string. Watch for null-terminator of prev string.
                    _stringIndexToResolvedOffsetMap[MetadataTokens.GetHeapOffset(entry.Value)] = position - (BlobUtilities.GetUTF8ByteCount(entry.Key) + 1);
                }
                else
                {
                    _stringIndexToResolvedOffsetMap[MetadataTokens.GetHeapOffset(entry.Value)] = position;
                    _stringWriter.WriteUTF8(entry.Key, allowUnpairedSurrogates: false);
                    _stringWriter.WriteByte(0);
                }

                prev = entry.Key;
            }
        }

        /// <summary>
        /// Sorts strings such that a string is followed immediately by all strings
        /// that are a suffix of it.  
        /// </summary>
        private class SuffixSort : IComparer<KeyValuePair<string, StringHandle>>
        {
            public int Compare(KeyValuePair<string, StringHandle> xPair, KeyValuePair<string, StringHandle> yPair)
            {
                string x = xPair.Key;
                string y = yPair.Key;

                for (int i = x.Length - 1, j = y.Length - 1; i >= 0 & j >= 0; i--, j--)
                {
                    if (x[i] < y[j])
                    {
                        return -1;
                    }

                    if (x[i] > y[j])
                    {
                        return +1;
                    }
                }

                return y.Length.CompareTo(x.Length);
            }
        }

        public void WriteHeapsTo(BlobBuilder writer)
        {
            WriteAligned(_stringWriter, writer);
            WriteAligned(_userStringWriter, writer);
            WriteAligned(_guidWriter, writer);
            WriteAlignedBlobHeap(writer);
        }

        private void WriteAlignedBlobHeap(BlobBuilder builder)
        {
            int alignment = BitArithmeticUtilities.Align(_blobHeapSize, 4) - _blobHeapSize;

            var writer = new BlobWriter(builder.ReserveBytes(_blobHeapSize + alignment));

            // Perf consideration: With large heap the following loop may cause a lot of cache misses 
            // since the order of entries in _blobs dictionary depends on the hash of the array values, 
            // which is not correlated to the heap index. If we observe such issue we should order 
            // the entries by heap position before running this loop.
            foreach (var entry in _blobs)
            {
                int heapOffset = MetadataTokens.GetHeapOffset(entry.Value);
                var blob = entry.Key;

                writer.Offset = heapOffset;
                writer.WriteCompressedInteger(blob.Length);
                writer.WriteBytes(blob);
            }

            writer.Offset = _blobHeapSize;
            writer.WriteBytes(0, alignment);
        }

        private static void WriteAligned(BlobBuilder source, BlobBuilder target)
        {
            int length = source.Count;
            target.LinkSuffix(source);
            target.WriteBytes(0, BitArithmeticUtilities.Align(length, 4) - length);
        }
    }
}

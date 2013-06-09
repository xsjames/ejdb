// ============================================================================================
//   .NET API for EJDB database library http://ejdb.org
//   Copyright (C) 2012-2013 Softmotions Ltd <info@softmotions.com>
//
//   This file is part of EJDB.
//   EJDB is free software; you can redistribute it and/or modify it under the terms of
//   the GNU Lesser General Public License as published by the Free Software Foundation; either
//   version 2.1 of the License or any later version.  EJDB is distributed in the hope
//   that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU Lesser General Public
//   License for more details.
//   You should have received a copy of the GNU Lesser General Public License along with EJDB;
//   if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330,
//   Boston, MA 02111-1307 USA.
// ============================================================================================
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;

namespace Ejdb.SON {

	public class BSONIterator : IDisposable {

		BinaryReader _input;
		int _doclen;
		BSONType _ctype = BSONType.UNKNOWN;
		string _entryKey;
		int _entryLen;
		bool _entryDataSkipped;
		BSONValue _entryDataValue;

		public bool Disposed {
			get {
				return (_input == null);
			}
		}

		public int DocumentLength {
			get { return this._doclen; }
			private set { this._doclen = value; }
		}

		public BSONIterator(byte[] bbuf) : this(new MemoryStream(bbuf)) {
		}

		public BSONIterator(Stream input) {
			if (!input.CanRead) {
				Dispose();
				throw new IOException("Input stream must be readable");
			}
			if (!input.CanSeek) {
				Dispose();
				throw new IOException("Input stream must be seekable");
			}
			this._input = new BinaryReader(input, Encoding.UTF8);
			this._ctype = BSONType.EOO;
			this._doclen = _input.ReadInt32();
			if (this._doclen < 5) {
				Dispose();
				throw new InvalidBSONDataException("Unexpected end of BSON document");
			}
		}

		BSONIterator(BinaryReader input, int doclen) {
			this._input = input;
			this._doclen = doclen;
			if (this._doclen < 5) {
				Dispose();
				throw new InvalidBSONDataException("Unexpected end of BSON document");
			}
		}

		~BSONIterator() {
			Dispose();
		}

		public void Dispose() {
			if (_input != null) {
				_input.Close();
				_input = null;
			}
		}

		void CheckDisposed() {
			if (Disposed) {
				throw new Exception("BSONIterator already disposed");
			}
		}

		public BSONType Next() {
			CheckDisposed();
			if (_ctype == BSONType.EOO) {
				return BSONType.EOO;
			}
			if (_ctype != BSONType.UNKNOWN) {
				SkipData();
			}
			byte bv = _input.ReadByte();
			if (!Enum.IsDefined(typeof(BSONType), bv)) {
				throw new InvalidBSONDataException("Unknown bson type: " + bv);
			}
			_entryDataSkipped = false;
			_entryDataValue = null;
			_entryKey = null;
			_ctype = (BSONType) bv;
			_entryLen = 0;
			if (_ctype != BSONType.EOO) {
				ReadKey();
			}
			switch (_ctype) {
				case BSONType.EOO:
					return BSONType.EOO;
				case BSONType.UNDEFINED:
				case BSONType.NULL:
					_entryLen = 0;
					break;
				case BSONType.BOOL:
					_entryLen = 1;
					break;
				case BSONType.INT:
					_entryLen = 4;
					break;
				case BSONType.LONG:
				case BSONType.DOUBLE:
				case BSONType.TIMESTAMP:
				case BSONType.DATE:
					_entryLen = 8;
					break;
				case BSONType.OID:
					_entryLen = 12;
					break;	
				case BSONType.STRING:
				case BSONType.CODE:
				case BSONType.SYMBOL:
					_entryLen = 4 + _input.ReadInt32();
					break;
				case BSONType.DBREF:
					_entryLen = 4 + 12 + _input.ReadInt32();
					break;
				case BSONType.BINDATA:
					_entryLen = 4 + 1 + _input.ReadInt32();
					break;
				case BSONType.OBJECT:
				case BSONType.ARRAY:
				case BSONType.CODEWSCOPE:
					_entryLen = 4 + _input.ReadInt32(); 
					break;
				case BSONType.REGEX:
					_entryLen = 0;
					break;
				default:
					throw new InvalidBSONDataException("Unknown entry type: " + _ctype);
			}
			return _ctype;
		}

		public BSONValue FetchCurrentValue() {
			CheckDisposed();
			if (_entryDataSkipped) {
				return _entryDataValue;
			}
			_entryDataSkipped = true;
			switch (_ctype) {
				case BSONType.EOO:					 
				case BSONType.UNDEFINED:
				case BSONType.NULL:
					_entryDataValue = new BSONValue(_ctype, _entryKey);
					break;	
				case BSONType.OID:
					Debug.Assert(_entryLen == 12);
					_entryDataValue = new BSONValue(_ctype, _entryKey, new BSONOid(_input));
					break;
				case BSONType.STRING:
				case BSONType.CODE:
				case BSONType.SYMBOL:
					{						
						Debug.Assert(_entryLen - 5 >= 0);						
						string sv = Encoding.UTF8.GetString(_input.ReadBytes(_entryLen - 5)); 
						_entryDataValue = new BSONValue(_ctype, _entryKey, sv);
						Debug.Assert(_input.ReadByte() == 0x00); //trailing zero byte
						break;
					}
				case BSONType.BOOL:
					_entryDataValue = new BSONValue(_ctype, _entryKey, _input.ReadBoolean());
					break;
				case BSONType.INT:
					_entryDataValue = new BSONValue(_ctype, _entryKey, _input.ReadInt32());
					break;
				case BSONType.OBJECT:
				case BSONType.ARRAY:
					{
						BSONDocument doc = (_ctype == BSONType.OBJECT ? new BSONDocument() : new BSONArray());
						BSONIterator sit = new BSONIterator(this._input, _entryLen - 4);
						while (sit.Next() != BSONType.EOO) {
							doc.Add(sit.FetchCurrentValue());
						}
						_entryDataValue = new BSONValue(_ctype, _entryKey, doc); 
						break;
					}
				case BSONType.DOUBLE:
					_entryDataValue = new BSONValue(_ctype, _entryKey, _input.ReadDouble());
					break;
				case BSONType.LONG:				
					_entryDataValue = new BSONValue(_ctype, _entryKey, _input.ReadInt64());
					break;			
				case BSONType.DATE:
					_entryDataValue = new BSONValue(_ctype, _entryKey, 
					                                BSONConstants.Epoch.AddMilliseconds(_input.ReadInt64()));
					break;
				case BSONType.TIMESTAMP:
					{
						int inc = _input.ReadInt32();
						int ts = _input.ReadInt32(); 
						_entryDataValue = new BSONValue(_ctype, _entryKey,
						                                new BSONTimestamp(inc, ts));
						break;
					}
				case BSONType.REGEX:
					{
						string re = ReadCString();
						string opts = ReadCString();
						_entryDataValue = new BSONValue(_ctype, _entryKey, 
						                                new BSONRegexp(re, opts));	
						break;
					}
				case BSONType.BINDATA:
					{
						byte subtype = _input.ReadByte();
						BSONBinData bd = new BSONBinData(subtype, _entryLen - 4, _input);
						_entryDataValue = new BSONValue(_ctype, _entryKey, bd);
						break;
					}
				case BSONType.DBREF:
					{
						SkipData(true);
						_entryDataValue = new BSONValue(_ctype, _entryKey);
						break;
					}
				case BSONType.CODEWSCOPE:
					{
						int cwlen = _entryLen - 4;
						Debug.Assert(cwlen > 5);
						int clen = _input.ReadInt32(); //code length
						string code = Encoding.UTF8.GetString(_input.ReadBytes(clen));
						BSONCodeWScope cw = new BSONCodeWScope(code);
						BSONIterator sit = new BSONIterator(_input, _input.ReadInt32());
						while (sit.Next() != BSONType.EOO) {
							cw.Add(sit.FetchCurrentValue());
						}
						_entryDataValue = new BSONValue(_ctype, _entryKey, cw);
						break;
					}

			}
			return _entryDataValue;
		}

		void SkipData(bool force = false) {
			if (_entryDataSkipped && !force) {
				return;
			}
			_entryDataValue = null;
			_entryDataSkipped = true;
			if (_ctype == BSONType.REGEX) {
				SkipCString();
				SkipCString();
				Debug.Assert(_entryLen == 0);
			} else if (_entryLen > 0) {
				long cpos = _input.BaseStream.Position;
				if ((cpos + _entryLen) != _input.BaseStream.Seek(_entryLen, SeekOrigin.Current)) {
					throw new IOException("Inconsitent seek within input BSON stream");
				}
				_entryLen = 0;
			} 
		}

		string ReadKey() {
			_entryKey = ReadCString();
			return _entryKey;
		}

		string ReadCString() {
			List<byte> sb = new List<byte>(64);
			byte bv;
			while ((bv = _input.ReadByte()) != 0x00) {
				sb.Add(bv);
			}
			return Encoding.UTF8.GetString(sb.ToArray());
		}

		void SkipCString() {
			while ((_input.ReadByte()) != 0x00)
				;
		}
	}
}


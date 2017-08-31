﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SQLitePCL;

namespace Microsoft.Data.Sqlite
{
    internal abstract class SqliteValueReader
    {
        public abstract int FieldCount { get; }

        protected abstract int GetSqliteType(int ordinal);

        public virtual bool IsDBNull(int ordinal)
            => GetSqliteType(ordinal) == raw.SQLITE_NULL;

        public virtual bool GetBoolean(int ordinal)
            => GetInt64(ordinal) != 0;

        public virtual byte GetByte(int ordinal)
            => (byte)GetInt64(ordinal);

        public virtual char GetChar(int ordinal)
            => (char)GetInt64(ordinal);

        public virtual DateTime GetDateTime(int ordinal)
        {
            var sqliteType = GetSqliteType(ordinal);
            switch (sqliteType)
            {
                case raw.SQLITE_NULL:
                    return GetNull<DateTime>();

                case raw.SQLITE_FLOAT:
                case raw.SQLITE_INTEGER:
                    return FromJulianDate(GetDouble(ordinal));

                default:
                    return DateTime.Parse(GetString(ordinal), CultureInfo.InvariantCulture);
            }
        }

        public virtual DateTimeOffset GetDateTimeOffset(int ordinal)
        {
            var sqliteType = GetSqliteType(ordinal);
            switch (sqliteType)
            {
                case raw.SQLITE_NULL:
                    return GetNull<DateTimeOffset>();

                case raw.SQLITE_FLOAT:
                case raw.SQLITE_INTEGER:
                    return new DateTimeOffset(FromJulianDate(GetDouble(ordinal)));

                default:
                    return DateTimeOffset.Parse(GetString(ordinal), CultureInfo.InvariantCulture);
            }
        }

        public virtual decimal GetDecimal(int ordinal)
            => IsDBNull(ordinal)
                ? GetNull<decimal>()
                : decimal.Parse(GetString(ordinal), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

        public abstract double GetDouble(int ordinal);

        public virtual float GetFloat(int ordinal)
            => (float)GetDouble(ordinal);

        public virtual Guid GetGuid(int ordinal)
        {
            var sqliteType = GetSqliteType(ordinal);
            switch (sqliteType)
            {
                case raw.SQLITE_NULL:
                    return GetNull<Guid>();

                case raw.SQLITE_BLOB:
                    var bytes = GetBlob(ordinal);
                    return bytes.Length == 16
                        ? new Guid(bytes)
                        : new Guid(Encoding.UTF8.GetString(bytes, 0, bytes.Length));

                default:
                    return new Guid(GetString(ordinal));
            }
        }

        protected virtual TimeSpan GetTimeSpan(int ordinal)
            => IsDBNull(ordinal)
                ? GetNull<TimeSpan>()
                : TimeSpan.Parse(GetString(ordinal));

        public virtual short GetInt16(int ordinal)
            => (short)GetInt64(ordinal);

        public virtual int GetInt32(int ordinal)
            => (int)GetInt64(ordinal);

        public abstract long GetInt64(int ordinal);

        public abstract string GetString(int ordinal);

        public virtual T GetFieldValue<T>(int ordinal)
        {
            if (IsDBNull(ordinal) && typeof(T).IsNullable())
            {
                return GetNull<T>();
            }

            var type = typeof(T).UnwrapNullableType().UnwrapEnumType();
            if (type == typeof(bool))
            {
                return (T)(object)GetBoolean(ordinal);
            }
            if (type == typeof(byte))
            {
                return (T)(object)GetByte(ordinal);
            }
            if (type == typeof(byte[]))
            {
                return (T)(object)GetBlob(ordinal);
            }
            if (type == typeof(char))
            {
                return (T)(object)GetChar(ordinal);
            }
            if (type == typeof(DateTime))
            {
                return (T)(object)GetDateTime(ordinal);
            }
            if (type == typeof(DateTimeOffset))
            {
                return (T)(object)GetDateTimeOffset(ordinal);
            }
            if (type == typeof(DBNull))
            {
                // NB: NULL values handled above
                throw new InvalidCastException();
            }
            if (type == typeof(decimal))
            {
                return (T)(object)GetDecimal(ordinal);
            }
            if (type == typeof(double))
            {
                return (T)(object)GetDouble(ordinal);
            }
            if (type == typeof(float))
            {
                return (T)(object)GetFloat(ordinal);
            }
            if (type == typeof(Guid))
            {
                return (T)(object)GetGuid(ordinal);
            }
            if (type == typeof(int))
            {
                return (T)(object)GetInt32(ordinal);
            }
            if (type == typeof(long))
            {
                return (T)(object)GetInt64(ordinal);
            }
            if (type == typeof(sbyte))
            {
                return (T)(object)((sbyte)GetInt64(ordinal));
            }
            if (type == typeof(short))
            {
                return (T)(object)GetInt16(ordinal);
            }
            if (type == typeof(string))
            {
                return (T)(object)GetString(ordinal);
            }
            if (type == typeof(TimeSpan))
            {
                return (T)(object)GetTimeSpan(ordinal);
            }
            if (type == typeof(uint))
            {
                return (T)(object)((uint)GetInt64(ordinal));
            }
            if (type == typeof(ulong))
            {
                return (T)(object)((ulong)GetInt64(ordinal));
            }
            if (type == typeof(ushort))
            {
                return (T)(object)((ushort)GetInt64(ordinal));
            }

            return (T)GetValue(ordinal);
        }

        public virtual object GetValue(int ordinal)
        {
            var sqliteType = GetSqliteType(ordinal);
            switch (sqliteType)
            {
                case raw.SQLITE_INTEGER:
                    return GetInt64(ordinal);

                case raw.SQLITE_FLOAT:
                    return GetDouble(ordinal);

                case raw.SQLITE_TEXT:
                    return GetString(ordinal);

                case raw.SQLITE_BLOB:
                    return GetBlob(ordinal);

                case raw.SQLITE_NULL:
                    return GetNull<object>();

                default:
                    Debug.Assert(false, "Unexpected column type: " + sqliteType);
                    return GetInt32(ordinal);
            }
        }

        public virtual int GetValues(object[] values)
        {
            int i;
            for (i = 0; i < FieldCount; i++)
            {
                values[i] = GetValue(i);
            }

            return i;
        }

        protected byte[] GetBlob(int ordinal)
            => IsDBNull(ordinal)
                ? GetNull<byte[]>()
                : GetBlobCore(ordinal) ?? Array.Empty<byte>();

        protected abstract byte[] GetBlobCore(int ordinal);

        protected virtual T GetNull<T>()
            => typeof(T) == typeof(DBNull)
                ? (T)(object)DBNull.Value
                : default(T);

        private static DateTime FromJulianDate(double julianDate)
        {
            // computeYMD
            var iJD = (long)(julianDate * 86400000.0 + 0.5);
            var Z = (int)((iJD + 43200000) / 86400000);
            var A = (int)((Z - 1867216.25) / 36524.25);
            A = Z + 1 + A - (A / 4);
            var B = A + 1524;
            var C = (int)((B - 122.1) / 365.25);
            var D = (36525 * (C & 32767)) / 100;
            var E = (int)((B - D) / 30.6001);
            var X1 = (int)(30.6001 * E);
            var day = B - D - X1;
            var month = E < 14 ? E - 1 : E - 13;
            var year = month > 2 ? C - 4716 : C - 4715;

            // computeHMS
            var s = (int)((iJD + 43200000) % 86400000);
            var fracSecond = s / 1000.0;
            s = (int)fracSecond;
            fracSecond -= s;
            var hour = s / 3600;
            s -= hour * 3600;
            var minute = s / 60;
            fracSecond += s - minute * 60;

            var second = (int)fracSecond;
            var millisecond = (int)Math.Round((fracSecond - second) * 1000.0);

            return new DateTime(year, month, day, hour, minute, second, millisecond);
        }
    }
}
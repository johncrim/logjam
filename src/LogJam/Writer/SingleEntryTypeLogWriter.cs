﻿// // --------------------------------------------------------------------------------------------------------------------
// <copyright file="SingleEntryTypeLogWriter.cs">
// Copyright (c) 2011-2015 logjam.codeplex.com.  
// </copyright>
// Licensed under the <a href="http://logjam.codeplex.com/license">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------


namespace LogJam.Writer
{
	using System;
	using System.Collections.Generic;

	using LogJam.Trace;


	/// <summary>
	/// Common implementation for <see cref="ILogWriter"/>s that can only have a single <typeparamref name="TEntry"/> type.
	/// </summary>
	/// <typeparamref name="TEntry">The log entry type</typeparamref>
	public abstract class SingleEntryTypeLogWriter<TEntry> : BaseLogWriter, IEntryWriter<TEntry>
		where TEntry : ILogEntry
	{

		protected SingleEntryTypeLogWriter(ITracerFactory setupTracerFactory)
			: base(setupTracerFactory)
		{}

		public override bool TryGetEntryWriter<TEntry1>(out IEntryWriter<TEntry1> entryWriter)
		{
			if (typeof(TEntry) == typeof(TEntry1))
			{
				entryWriter = (IEntryWriter<TEntry1>) this;
				return true;
			}
			else
			{
				entryWriter = null;
				return false;
			}
		}

		public override IEnumerable<KeyValuePair<Type, IEntryWriter>> EntryWriters
		{
			get { return new[] { new KeyValuePair<Type, IEntryWriter>(typeof(TEntry), this) }; }
		}

		#region IEntryWriter<TEntry>

		public virtual bool IsEnabled { get { return IsStarted; } }

		public Type LogEntryType { get { return typeof(TEntry); } }

		public abstract void Write(ref TEntry entry);

		#endregion

		protected override void InternalStart()
		{
			// Don't call base.InternalStart() - because it's redundant to start the EntryWriter
		}

		protected override void InternalStop()
		{
			// Don't call base.InternalStop() - because it's redundant to stop the EntryWriter
		}

	}

}
﻿// // --------------------------------------------------------------------------------------------------------------------
// <copyright file="IBufferingLogWriter.cs">
// Copyright (c) 2011-2015 logjam.codeplex.com.  
// </copyright>
// Licensed under the <a href="http://logjam.codeplex.com/license">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------


namespace LogJam.Writer
{
	using System;
	using System.Diagnostics.Contracts;


	/// <summary>
	/// Adds support for "smart" flushing logic to log writers that buffer output, and need to know
	/// when it makes sense to flush their buffers.
	/// </summary>
	[ContractClass(typeof(BufferingLogWriterContract))]
	public interface IBufferingLogWriter : ILogWriter
	{

		/// <summary>
		/// Gets and sets a function that the logwriter can use to determine whether it should flush
		/// its buffer or not.
		/// </summary>
		Func<bool> FlushPredicate { get; set; }

	}

	[ContractClassFor(typeof(IBufferingLogWriter))]
	internal abstract class BufferingLogWriterContract : IBufferingLogWriter
	{

		public bool Enabled { get { throw new NotImplementedException(); } }

		public bool IsSynchronized { get { throw new NotImplementedException(); } }

		public Func<bool> FlushPredicate
		{
			get
			{
				Contract.Ensures(Contract.Result<Func<bool>>() != null);
				throw new NotImplementedException();
			}
			set
			{
				Contract.Requires<ArgumentNullException>(value != null);
				throw new NotImplementedException();
			}
		}

	}

}
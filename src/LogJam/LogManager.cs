﻿// // --------------------------------------------------------------------------------------------------------------------
// <copyright file="LogManager.cs">
// Copyright (c) 2011-2014 logjam.codeplex.com.  
// </copyright>
// Licensed under the <a href="http://logjam.codeplex.com/license">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------


namespace LogJam
{
	using LogJam.Config;
	using LogJam.Internal;
	using LogJam.Trace;
	using LogJam.Util;
	using LogJam.Writer;
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.Contracts;
	using System.Linq;
	using System.Threading;

	using LogJam.Config.Initializer;


	/// <summary>
	/// Manager object that manages all <see cref="ILogWriter"/>s for a context - eg process, or test.
	/// </summary>
	public sealed class LogManager : BaseLogJamManager
	{
		#region Static fields

		private static LogManager s_instance;

		#endregion

		#region Static properties

		/// <summary>
		/// Sets or gets an AppDomain-global <see cref="LogManager"/>.
		/// </summary>
		public static LogManager Instance
		{
			get
			{
				if (s_instance != null)
				{
					return s_instance;
				}

				Interlocked.CompareExchange(ref s_instance, new LogManager(), null);
				return s_instance;
			}
			internal set
			{
				var previousInstance = Interlocked.Exchange(ref s_instance, value);
				if (previousInstance != null)
				{
					previousInstance.Stop();
				}
			}
		}

		#endregion

		#region Instance fields

		/// <summary>
		/// An internal <see cref="ITracerFactory"/>, which is used only for tracing LogJam operations.
		/// </summary>
		private readonly ITracerFactory _setupTracerFactory;

		private readonly LogManagerConfig _config;

		private readonly List<BackgroundMultiLogWriter> _backgroundMultiLogWriters;

		private readonly Dictionary<ILogWriterConfig, ILogWriter> _logWriters; 

		#endregion

		#region Constructors and Destructors

		/// <summary>
		/// Creates a new <see cref="LogManager"/> instance using an empty initial configuration.
		/// </summary>
		public LogManager()
			: this(new LogManagerConfig())
		{}

		/// <summary>
		/// Creates a new <see cref="LogManager"/> instance using the specified <paramref name="logManagerConfig"/> for configuration, and an optional <paramref name="setupTracerFactory"/> for logging LogJam setup and internal operations.
		/// </summary>
		/// <param name="logManagerConfig">The <see cref="LogManagerConfig"/> that describes how this <see cref="LogManager"/> should be setup.</param>
		/// <param name="setupTracerFactory">The <see cref="ITracerFactory"/> to use for tracking internal operations.</param>
		public LogManager(LogManagerConfig logManagerConfig, ITracerFactory setupTracerFactory = null)
		{
			Contract.Requires<ArgumentNullException>(logManagerConfig != null);

			_setupTracerFactory = setupTracerFactory ?? new SetupLog();
			_config = logManagerConfig;
			_backgroundMultiLogWriters = new List<BackgroundMultiLogWriter>();
			_logWriters = new Dictionary<ILogWriterConfig, ILogWriter>();
		}

		/// <summary>
		/// Creates a new <see cref="LogManager"/> instance using the specified <paramref name="logWriterConfigs"/> to configure logging.
		/// </summary>
		/// <param name="logWriterConfigs">A set of 1 or more <see cref="ILogWriterConfig"/> instances to use to configure log writers.</param>
		public LogManager(params ILogWriterConfig[] logWriterConfigs)
			: this(new LogManagerConfig(logWriterConfigs))
		{}

		/// <summary>
		/// Creates a new <see cref="LogManager"/> instance using the specified <paramref name="logWriters"/>.
		/// </summary>
		/// <param name="logWriters">A set of 1 or more <see cref="ILogWriter"/> instances to include in the <see cref="LogManager"/>.</param>
		public LogManager(params ILogWriter[] logWriters)
			: this(logWriters.Select(logWriter => (ILogWriterConfig) new UseExistingLogWriterConfig(logWriter)).ToArray())
		{
			// Get the first SetupLog from the passed in logwriters
			var setupTracerFactory = GetSetupTracerFactoryForComponents(logWriters.OfType<ILogJamComponent>());
			_setupTracerFactory = setupTracerFactory ?? _setupTracerFactory ?? new SetupLog();
		}

		~LogManager()
		{
			if (! IsDisposed)
			{
				var tracer = SetupTracerFactory.TracerFor(this);
				tracer.Error("In finalizer (~BackgroundMultiLogWriter) - forgot to Dispose()?");
				Dispose(false);
			}
		}

		#endregion

		/// <summary>
		/// The <see cref="LogManagerConfig"/> used to configure this <c>LogManager</c>.
		/// </summary>
		public LogManagerConfig Config { get { return _config; } }

		public override ITracerFactory SetupTracerFactory { get { return _setupTracerFactory; } }

		/// <summary>
		/// Returns the collection of <see cref="TraceEntry"/>s logged through <see cref="SetupTracerFactory"/>.
		/// </summary>
		public override IEnumerable<TraceEntry> SetupLog
		{
			get { return _setupTracerFactory as IEnumerable<TraceEntry> ?? Enumerable.Empty<TraceEntry>(); }
		}

		/// <summary>
		/// Adds a <see cref="BackgroundMultiLogWriter"/> so a reference is maintained (to keep it alive), and so
		/// it can be stopped when the <c>LogManager</c> is stopped.
		/// </summary>
		/// <param name="backgroundMultiLogWriter"></param>
		internal void AddBackgroundMultiLogWriter(BackgroundMultiLogWriter backgroundMultiLogWriter)
		{
			Contract.Requires<ArgumentNullException>(backgroundMultiLogWriter != null);
			_backgroundMultiLogWriters.Add(backgroundMultiLogWriter);
		}

		protected override void InternalStart()
		{
			lock (this)
			{
				var logManagerTracer = SetupTracerFactory.TracerFor(this);

				if (_logWriters.Count > 0)
				{
					logManagerTracer.Debug("Stopping LogManager before re-starting it...");
					Stop();
				}

				// Create LogWriters from config
				foreach (ILogWriterConfig logWriterConfig in Config.Writers)
				{
					if (_logWriters.ContainsKey(logWriterConfig))
					{
						// Occurs when the logWriterConfig already exists - this shouldn't happen
						var tracer = SetupTracerFactory.TracerFor(logWriterConfig);
						tracer.Severe("LogWriterConfig {0} is already active - this shouldn't happen.  Skipping it...", logWriterConfig);
						continue;
					}

					ILogWriter logWriter = CreateLogWriter(logWriterConfig);

					_logWriters.Add(logWriterConfig, logWriter);

					if (logWriterConfig.DisposeOnStop)
					{
						DisposeOnStop(logWriter);
					}
				}

				// Start background writers and log writers
				_backgroundMultiLogWriters.SafeStart(SetupTracerFactory);
				_logWriters.Values.SafeStart(SetupTracerFactory);
			}
		}

		/// <summary>
		/// Create an <see cref="ILogWriter"/> from <paramref name="logWriterConfig"/>, then proxy it as configured and log any
		/// errors in <see cref="SetupLog"/>.
		/// </summary>
		/// <param name="logWriterConfig"></param>
		/// <returns></returns>
		internal ILogWriter CreateLogWriter(ILogWriterConfig logWriterConfig)
		{
			ILogWriter logWriter = null;
			
			// A lightweight service locator scoped to a single LogWriter pipeline
			DependencyDictionary logWriterDependencyDictionary = new DependencyDictionary();
			logWriterDependencyDictionary.Add<ITracerFactory>(SetupTracerFactory);
			logWriterDependencyDictionary.Add<LogManager>(this);
			logWriterDependencyDictionary.Add<ILogWriterConfig>(logWriterConfig);
			logWriterDependencyDictionary.Add(logWriterConfig.GetType(), logWriterConfig);

			try
			{
				logWriter = logWriterConfig.CreateLogWriter(SetupTracerFactory);
				if (logWriter == null)
				{	// Can occur when the logwriter cannot be created; the config object should log errors.
					// TODO: Ensure that the error state is represented
					return null;
				}
				logWriterDependencyDictionary.Add(logWriter.GetType(), logWriter);

				// Build pipeline and support dependency resolution using ILogWriterInitializers
				logWriter = BuildLogWriterPipeline(logWriterConfig, logWriter, logWriterDependencyDictionary);
				logWriterDependencyDictionary.AddIfNotDefined(typeof(ILogWriter), logWriter);
				logWriterDependencyDictionary.EndInitialization();

				ConnectImportsForLogWriterPipeline(logWriterConfig, logWriterDependencyDictionary);
			}
			catch (Exception excp)
			{
				// TODO: Store initialization failure status
				var tracer = SetupTracerFactory.TracerFor(logWriterConfig);
				tracer.Severe(excp, "Exception creating logwriter from config: {0}", logWriterConfig);
				logWriter = null;
			}

			return logWriter;
		}

		internal ILogWriter BuildLogWriterPipeline(ILogWriterConfig logWriterConfig, ILogWriter logWriter, DependencyDictionary dependencyDictionary)
		{
			foreach (var pipelineInitializer in logWriterConfig.Initializers.Concat(Config.Initializers).OfType<ILogWriterPipelineInitializer>())
			{
				logWriter = pipelineInitializer.InitializeLogWriter(SetupTracerFactory, logWriter, dependencyDictionary);
				dependencyDictionary.AddIfNotDefined(logWriter.GetType(), logWriter);
			}
			return logWriter;
		}

		internal void ConnectImportsForLogWriterPipeline(ILogWriterConfig logWriterConfig, DependencyDictionary dependencyDictionary)
		{
			foreach (var importInitializer in logWriterConfig.Initializers.Concat(Config.Initializers).OfType<IImportInitializer>())
			{
				importInitializer.ImportDependencies(SetupTracerFactory, dependencyDictionary);
			}
		}

		/// <summary>
		/// Stops all log writers managed by this <see cref="LogManager"/>.
		/// </summary>
		protected override void InternalStop()
		{
			lock (this)
			{
				_logWriters.SafeStop(SetupTracerFactory);
				_logWriters.Clear();
				_backgroundMultiLogWriters.SafeStop(SetupTracerFactory);
				_backgroundMultiLogWriters.Clear();
			}
		}

		protected override void InternalReset()
		{
			// Stop has already been called

			_config.Reset();
		}

		/// <summary>
		/// Returns all successfully started <see cref="IEntryWriter{TEntry}"/>s associated with this <c>LogManager</c>.
		/// </summary>
		/// <typeparam name="TEntry">The logentry type written by the returned <see cref="IEntryWriter{TEntry}"/>s.</typeparam>
		/// <returns>All successfully started <see cref="IEntryWriter{TEntry}"/>s that are type-compatible with <typeparamref name="TEntry"/>.  May return an empty enumerable.</returns>
		/// <remarks>Even if Start() wasn't 100% successful, we still return any logwriters that were successfully started.</remarks>
		public IEnumerable<IEntryWriter<TEntry>> GetEntryWriters<TEntry>() where TEntry : ILogEntry
		{
			// Even if Start() wasn't 100% successful, we still return any logwriters that are available.
			EnsureStarted();

			lock (this)
			{
				var listLogWriters = new List<IEntryWriter<TEntry>>();
				_logWriters.Values.GetEntryWriters(listLogWriters);
				return listLogWriters;
			}
		}

		/// <summary>
		/// Returns a single <see cref="IEntryWriter{TEntry}"/> that writes to all successfully started <see cref="IEntryWriter{TEntry}"/>s associated with this <c>LogManager</c>.
		/// </summary>
		/// <typeparam name="TEntry">The logentry type written by the returned <see cref="IEntryWriter{TEntry}"/>s.</typeparam>
		/// <returns>A single <see cref="IEntryWriter{TEntry}"/> that writes to all successfully started <see cref="IEntryWriter{TEntry}"/>s that are type-compatible with <typeparamref name="TEntry"/>.</returns>
		public IEntryWriter<TEntry> GetEntryWriter<TEntry>() where TEntry : ILogEntry
		{
			IEntryWriter<TEntry>[] entryWriters = GetEntryWriters<TEntry>().ToArray();
			if (entryWriters.Length == 1)
			{
				return entryWriters[0];
			}
			else if (entryWriters.Length == 0)
			{
				return new NoOpEntryWriter<TEntry>();
			}
			else
			{
				return new FanOutEntryWriter<TEntry>(entryWriters);
			}
		}

		/// <summary>
		/// Returns the <see cref="ILogWriter"/> created from the specified <paramref name="logWriterConfig"/>.
		/// </summary>
		/// <param name="logWriterConfig"></param>
		/// <returns>The <see cref="ILogWriter"/> created from <paramref name="logWriterConfig"/> if one exists; otherwise null.</returns>
		/// <exception cref="KeyNotFoundException">If no value in <c>Config.Writers</c> is equal to <paramref name="logWriterConfig"/></exception>
		public ILogWriter GetLogWriter(ILogWriterConfig logWriterConfig)
		{
			Contract.Requires<ArgumentNullException>(logWriterConfig != null);

			// Even if Start() wasn't 100% successful, we still return any logwriters that were successfully started.
			EnsureStarted();

			ILogWriter logWriter = null;
			if (!_logWriters.TryGetValue(logWriterConfig, out logWriter))
			{
				throw new KeyNotFoundException("logWriterConfig not found.");
			}
			return logWriter;
		}

		/// <summary>
		/// Returns the <see cref="IEntryWriter{TEntry}"/> matching with the specified <see cref="ILogWriterConfig"/>.  
		/// </summary>
		/// <typeparam name="TEntry">The logentry type written by the returned <see cref="IEntryWriter{TEntry}"/>.</typeparam>
		/// <param name="logWriterConfig">An <see cref="ILogWriterConfig"/> instance.</param>
		/// <returns>An <see cref="IEntryWriter{TEntry}"/> for <paramref name="logWriterConfig"/> and of entry type <typeparamref name="TEntry"/>.
		/// If the <c>logWriterConfig</c> is valid, but the log writer failed to start or doesn't contain an entry writer of the specified type,
		/// a <see cref="NoOpEntryWriter{TEntry}"/> is returned.</returns>
		/// <exception cref="KeyNotFoundException">If no value in <c>Config.Writers</c> is equal to <paramref name="logWriterConfig"/></exception>
		/// <remarks>This method throws exceptions if the call is invalid, but
		/// does not throw an exception if the returned logwriter failed to start.</remarks>
		public IEntryWriter<TEntry> GetEntryWriter<TEntry>(ILogWriterConfig logWriterConfig) where TEntry : ILogEntry
		{
			Contract.Requires<ArgumentNullException>(logWriterConfig != null);

			ILogWriter logWriter = GetLogWriter(logWriterConfig);
			if (logWriter == null)
			{	// This occurs when entryWriter.Start() fails.  In this case, the desired behavior is to return a functioning logwriter.
				var tracer = SetupTracerFactory.TracerFor(this);
				tracer.Warn("Returning a NoOpEntryWriter<{0}> for log writer config: {1} - check start errors.", typeof(TEntry).Name, logWriterConfig);
				return new NoOpEntryWriter<TEntry>();
			}
			
			IEntryWriter<TEntry> entryWriter;
			if (! logWriter.TryGetEntryWriter(out entryWriter))
			{
				var tracer = SetupTracerFactory.TracerFor(this);
				tracer.Warn("Returning a NoOpEntryWriter<{0}> for log writer {1} - log writer did not contain an entry writer for log entry type {0}.", typeof(TEntry).Name, logWriterConfig);
				return new NoOpEntryWriter<TEntry>();
			}
			return entryWriter;
		}

	}

}
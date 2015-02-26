﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TraceManager.cs">
// Copyright (c) 2011-2014 logjam.codeplex.com.  
// </copyright>
// Licensed under the <a href="http://logjam.codeplex.com/license">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------

namespace LogJam.Trace
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics.Contracts;
	using System.Linq;
	using System.Threading;

	using LogJam.Config;
	using LogJam.Trace.Config;
	using LogJam.Trace.Switches;
	using LogJam.Writer;


	/// <summary>
	/// Entry point for everything related to tracing.
	/// </summary>
	public sealed class TraceManager : BaseLogJamManager, ITracerFactory
	{
		#region Static fields

		private static readonly Lazy<TraceManager> s_instance;

		#endregion

		#region Instance fields

		private readonly TraceManagerConfig _traceConfig;

		private readonly Dictionary<string, WeakReference> _tracers = new Dictionary<string, WeakReference>(100);

		private readonly List<Tuple<TraceWriterConfig, ILogWriter<TraceEntry>>> _activeTraceLogWriters = new List<Tuple<TraceWriterConfig, ILogWriter<TraceEntry>>>();

		/// <summary>
		/// TraceManager uses a <see cref="LogManager"/> to manage the <see cref="ILogWriter"/>s that <see cref="TraceEntry"/>s are written to.
		/// </summary>
		private readonly LogManager _logManager;

		#endregion

		static TraceManager()
		{
			s_instance = new Lazy<TraceManager>(() => new TraceManager());
		}

		/// <summary>
		/// Returns an AppDomain-global <see cref="TraceManager"/>.
		/// </summary>
		public static TraceManager Instance { get { return s_instance.Value; } }

		#region Constructors and Destructors

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> instance using default configuration.
		/// </summary>
		public TraceManager()
			: this(new TraceManagerConfig())
		{
			// TODO: Check for local or remote config?
		}

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> configured to use <paramref name="logWriterConfig"/> and
		/// <paramref name="traceSwitch"/> for all <see cref="Tracer"/>s.
		/// </summary>
		/// <param name="logWriterConfig">The <see cref="LogWriterConfig{TEntry}"/> to use to configure tracing.</param>
		/// <param name="traceSwitch">A <see cref="ITraceSwitch"/> to use for all <see cref="Tracer"/>s.  If
		/// <c>null</c>, all <see cref="Tracer"/> calls of severity <see cref="TraceLevel.Info"/> or higher are written.</param>
		/// <param name="tracerNamePrefix">The <see cref="Tracer.Name"/> prefix to use.  Tracing will not occur if the
		/// <c>Tracer.Name</c> doesn't match this prefix.  By default, <see cref="Tracer.All"/> is used.</param>
		public TraceManager(LogWriterConfig<TraceEntry> logWriterConfig, ITraceSwitch traceSwitch = null, string tracerNamePrefix = Tracer.All)
		{
			Contract.Requires<ArgumentNullException>(logWriterConfig != null);

			if (traceSwitch == null)
			{
				traceSwitch = new ThresholdTraceSwitch(TraceLevel.Info);
			}

			_logManager = new LogManager();
			DisposeOnStop(_logManager); // bc the LogManager is owned by this
			_traceConfig = new TraceManagerConfig(new TraceWriterConfig(logWriterConfig)
			                                      {
													  Switches = { { tracerNamePrefix, traceSwitch } }
			                                      });
		}

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> configured to use <paramref name="logWriter"/> and
		/// <paramref name="traceSwitch"/> for all <see cref="Tracer"/>s.
		/// </summary>
		/// <param name="logWriter">The <see cref="ILogWriter{TEntry}"/> to use.</param>
		/// <param name="traceSwitch">A <see cref="ITraceSwitch"/> to use for all <see cref="Tracer"/>s.  If
		/// <c>null</c>, all <see cref="Tracer"/> calls of severity <see cref="TraceLevel.Info"/> or higher are written.</param>
		/// <param name="tracerNamePrefix">The <see cref="Tracer.Name"/> prefix to use.  Tracing will not occur if the
		/// <c>Tracer.Name</c> doesn't match this prefix.  By default, <see cref="Tracer.All"/> is used.</param>
		public TraceManager(ILogWriter<TraceEntry> logWriter, ITraceSwitch traceSwitch = null, string tracerNamePrefix = Tracer.All)
			: this(new UseExistingLogWriterConfig<TraceEntry>(logWriter), traceSwitch, tracerNamePrefix)
		{}

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> configured to use <paramref name="logWriter"/> and
		/// <paramref name="traceSwitch"/> for all <see cref="Tracer"/>s.
		/// </summary>
		/// <param name="logWriter">The <see cref="ILogWriter{TEntry}"/> to use.</param>
		/// <param name="switches">Defines the trace switches to use with <paramref name="logWriter"/>.</param>
		public TraceManager(ILogWriter<TraceEntry> logWriter, SwitchSet switches)
			: this(new TraceWriterConfig(logWriter, switches))
		{ }

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> configured to use <paramref name="logWriter"/> and trace
		/// everything that meets or exceeds <paramref name="traceThreshold"/>.
		/// </summary>
		/// <param name="logWriter">The <see cref="ILogWriter{TEntry}"/> to use.</param>
		/// <param name="traceThreshold">The minimum <see cref="TraceLevel"/> that will be logged.</param>
		/// <param name="tracerNamePrefix">The <see cref="Tracer.Name"/> prefix to use.  Tracing will not occur if the
		/// <c>Tracer.Name</c> doesn't match this prefix.  By default, <see cref="Tracer.All"/> is used.</param>
		public TraceManager(ILogWriter<TraceEntry> logWriter, TraceLevel traceThreshold, string tracerNamePrefix = Tracer.All)
			: this(new UseExistingLogWriterConfig<TraceEntry>(logWriter), new ThresholdTraceSwitch(traceThreshold))
		{ }

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> instance using the specified <paramref name="traceWriterConfig"/>.
		/// </summary>
		/// <param name="traceWriterConfig">The <see cref="TraceWriterConfig"/> to use for this <c>TraceManager</c>.</param>
		public TraceManager(TraceWriterConfig traceWriterConfig)
			: this(new TraceManagerConfig(traceWriterConfig))
		{}

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> instance using the specified <paramref name="configuration"/>.
		/// </summary>
		/// <param name="configuration">The <see cref="TraceManagerConfig"/> to use to configure this <c>TraceManager</c>.</param>
		public TraceManager(TraceManagerConfig configuration)
			: this(new LogManager(), configuration)
		{
			// This TraceManager owns the LogManager, so dispose it on this.Dispose()
			DisposeOnStop(_logManager);
		}

		/// <summary>
		/// Creates a new <see cref="TraceManager"/> instance using the specified<paramref name="logManager"/> and <paramref name="configuration"/>.
		/// </summary>
		/// <param name="logManager">The <see cref="LogManager"/> associated with this <see cref="TraceManager"/>.</param>
		/// <param name="configuration">The <see cref="TraceManagerConfig"/> to use to configure this <c>TraceManager</c>.</param>
		public TraceManager(LogManager logManager, TraceManagerConfig configuration)
		{
			Contract.Requires<ArgumentNullException>(logManager != null);
			Contract.Requires<ArgumentNullException>(configuration != null);

			_logManager = logManager;
			_traceConfig = configuration;
		}

		protected override void InternalStart()
		{
			// Copy all LogWriterConfigs in the TraceWriterConfigs to the LogManager, if not already there
			IEnumerable<ILogWriterConfig> traceLogWriterConfigs = Config.Writers.Select(writerConfig => writerConfig.LogWriterConfig);
			bool restartNotRequired = _logManager.Config.Writers.IsProperSupersetOf(traceLogWriterConfigs);

			lock (this)
			{
				if (restartNotRequired)
				{
					// No config changes needed, just start it if not already started
					_logManager.EnsureStarted();
				}
				else
				{
					// Add the LogWriterConfigs, and restart the LogManager
					_logManager.Config.Writers.UnionWith(traceLogWriterConfigs);
					_logManager.Start();			
				}

				// Create all the TraceWriters associated with each config entry
				_activeTraceLogWriters.Clear();
				foreach (TraceWriterConfig traceWriterConfig in Config.Writers)
				{
					ILogWriter<TraceEntry> traceLogWriter = LogManager.GetLogWriter<TraceEntry>(traceWriterConfig.LogWriterConfig);
					if (traceLogWriter != null)
					{
						_activeTraceLogWriters.Add(new Tuple<TraceWriterConfig, ILogWriter<TraceEntry>>(traceWriterConfig, traceLogWriter));
					}
				}

				// Reset TraceWriter for each Tracer
				ForEachTracer(tracer => tracer.Configure(GetTraceWritersFor(tracer.Name)));
			}
		}

		protected override void InternalStop()
		{
			lock (this)
			{
				// Commented out b/c TraceWriter is not currently IStartable
				//foreach (var traceWriter in _activeTraceLogWriters.Select(kvp => kvp.Value))
				//{
				//	var startableTraceWriter = traceWriter as IStartable;
				//	if (startableTraceWriter != null)
				//	{
				//		startableTraceWriter.Stop();
				//	}
				//}
				_activeTraceLogWriters.Clear();

				// Set all Tracers to write to a NoOpTraceWriter
				var noopTraceWriter = new NoOpTraceWriter();
				ForEachTracer(tracer => tracer.Writer = noopTraceWriter);
			}
		}

		#endregion

		#region Public Properties

		/// <summary>
		/// Returns the <see cref="TraceManagerConfig"/> used to configure this <see cref="TraceManager"/>.
		/// </summary>
		public TraceManagerConfig Config { get { return _traceConfig; } }

		/// <summary>
		/// Returns the <see cref="LogManager"/> associated with this <see cref="TraceManager"/>.
		/// </summary>
		public LogManager LogManager { get { return _logManager; } }

		#endregion

		#region Public Methods and Operators

		/// <summary>
		/// Gets or creates a <see cref="Tracer"/> using the specified <paramref name="name"/>.
		/// </summary>
		/// <param name="name">The <see cref="Tracer.Name"/> to match or create.</param>
		/// <returns>
		/// The <see cref="Tracer"/>.
		/// </returns>
		public Tracer GetTracer(string name)
		{
			EnsureStarted();

			if (name == null)
			{
				name = string.Empty;
			}

			name = name.Trim();

			// Lookup the Tracer, or add a new one
			WeakReference weakRefTracer;
			lock (this)
			{
				if (_tracers.TryGetValue(name, out weakRefTracer))
				{
					object objTracer = weakRefTracer.Target;
					if (objTracer == null)
					{
						_tracers.Remove(name);
					}
					else
					{
						// Return the existing Tracer
						return (Tracer) objTracer;
					}
				}

				// Create a new Tracer and register it
				Tracer tracer = new Tracer(name, GetTraceWritersFor(name));
				_tracers[name] = new WeakReference(tracer);
				return tracer;
			}
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Handles performing an action on each registered <see cref="Tracer"/>, and cleaning up any that have
		/// been GCed.
		/// </summary>
		/// <param name="action"></param>
		private void ForEachTracer(Action<Tracer> action)
		{
			lock (this)
			{
				List<string> keysToRemove = new List<string>();
				foreach (var kvp in _tracers)
				{
					WeakReference weakRefTracer = kvp.Value;
					object objTracer = weakRefTracer.Target;
					Tracer tracer = objTracer as Tracer;
					if (tracer == null)
					{
						keysToRemove.Add(kvp.Key);
					}
					else
					{
						action(tracer);
					}
				}

				foreach (var keyToRemove in keysToRemove)
				{
					_tracers.Remove(keyToRemove);
				}
			}
		}

		private TraceWriter[] GetTraceWritersFor(string tracerName)
		{
			// REVIEW: We could cache the TraceWriters so that the same instances are returned when the switch + logwriter instances are the same
			var traceWriters = new List<TraceWriter>();
			foreach (var traceWriterTuple in _activeTraceLogWriters)
			{
				ITraceSwitch traceSwitch;
				if (traceWriterTuple.Item1.GetSwitchList().FindBestMatchingSwitch(tracerName, out traceSwitch))
				{
					traceWriters.Add(new TraceWriter(traceSwitch, traceWriterTuple.Item2, SetupTracerFactory));
				}
			}
			return traceWriters.ToArray();
		}

		//private void OnTracerConfigAddedOrRemoved(object sender, ConfigChangedEventArgs<TracerConfig> e)
		//{
		//	// Re-configure each existing Tracer that prefix matches the TracerConfig being added or removed
		//	foreach (Tracer tracer in FindMatchingTracersFor(e.ConfigChanged))
		//	{
		//		// REVIEW: It's probably best if TracerConfig TraceSwitch and traceLogWriter are always populated
		//		tracer.Configure(e.ConfigChanged.TraceWriters);
		//	}
		//}

		#endregion
		#region BaseLogJamManager overrides

		internal override ITracerFactory SetupTracerFactory { get { return LogManager.SetupTracerFactory; } }

		public override IEnumerable<TraceEntry> SetupTraces { get { return LogManager.SetupTraces; } }

		#endregion

	}
}
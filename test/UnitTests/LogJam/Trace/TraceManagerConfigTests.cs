﻿// // --------------------------------------------------------------------------------------------------------------------
// <copyright file="TraceManagerConfigTests.cs">
// Copyright (c) 2011-2014 logjam.codeplex.com.  
// </copyright>
// Licensed under the <a href="http://logjam.codeplex.com/license">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------


namespace LogJam.UnitTests.Trace
{
	using LogJam.Config;
	using LogJam.Config.Json;
	using LogJam.Format;
	using LogJam.Trace;
	using LogJam.Trace.Config;
	using LogJam.Trace.Format;
	using LogJam.Trace.Switches;
	using LogJam.Util;
	using LogJam.Writer;
	using Newtonsoft.Json;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Xml.Serialization;
	using Xunit;
	using Xunit.Extensions;


	/// <summary>
	/// Exercises use cases for <see cref="TraceManager.Config"/> modification.
	/// </summary>
	public sealed class TraceManagerConfigTests
	{
		/// <summary>
		/// By default, info and greater messages are written to a <see cref="DebuggerLogWriter"/>.
		/// </summary>
		[Fact]
		public void VerifyDefaultTraceManagerConfig()
		{
			using (var traceManager = new TraceManager())
			{
				var tracer = traceManager.TracerFor(this);
				Assert.True(tracer.IsInfoEnabled());
				Assert.False(tracer.IsVerboseEnabled());
				tracer.Info("Info message to debugger");

				AssertEquivalentToDefaultTraceManagerConfig(traceManager);
			}
		}

		public static void AssertEquivalentToDefaultTraceManagerConfig(TraceManager traceManager)
		{
			var tracer = traceManager.GetTracer("");
			Assert.True(tracer.IsInfoEnabled());
			Assert.False(tracer.IsVerboseEnabled());

			// Walk the Tracer object to ensure everything is as expected for default configuration
			Assert.IsType<TraceWriter>(tracer.Writer);
			var traceWriter = (TraceWriter) tracer.Writer;
			Assert.IsType<TextWriterLogWriter<TraceEntry>>(traceWriter.InnerLogWriter);
			var logWriter = (TextWriterLogWriter<TraceEntry>) traceWriter.InnerLogWriter;
			Assert.IsType<DebuggerTraceFormatter>(logWriter.Formatter);
			Assert.IsType<DebuggerTextWriter>(logWriter.TextWriter);
		}

		[Fact]
		public void TraceWithTimestampsToConsole()
		{
			using (var traceManager = new TraceManager(new ConsoleTraceWriterConfig()
			                                           {
				                                           Formatter = new DebuggerTraceFormatter() { IncludeTimestamp = true }
			                                           }))
			{
				var tracer = traceManager.TracerFor(this);
				Assert.True(tracer.IsInfoEnabled());
				Assert.False(tracer.IsVerboseEnabled());
				//Assert.Single(tracer.Writers);
				//Assert.IsType<DebuggerLogWriter>(tracer.Writers[0].InnerLogWriter);
				tracer.Info("Info message to console");
				tracer.Debug("Debug message not written to console");
			}
		}

		/// <summary>
		/// Ensures that everything works as expected when no TraceWriterConfig elements are configured.
		/// </summary>
		[Fact]
		public void NoTraceWritersConfiguredWorks()
		{
			var traceManagerConfig = new TraceManagerConfig();
			traceManagerConfig.Writers.Clear();
			using (var traceManager = new TraceManager(traceManagerConfig))
			{
				traceManager.Start();
				var tracer = traceManager.TracerFor(this);

				Assert.False(tracer.IsInfoEnabled());

				tracer.Info("Info");
			}
		}

		//[Fact]
		//public void RootThresholdCanBeSetOnInitialization()
		//{
		//	var listTraceLog = new ListLogWriter<TraceEntry>();
		//	// Trace output has threshold == Error
		//	using (var traceManager = new TraceManager(listTraceLog, new ThresholdTraceSwitch(TraceLevel.Error)))
		//	{
		//		var tracer = traceManager.TracerFor(this);
		//		Assert.False(tracer.IsInfoEnabled());
		//		Assert.False(tracer.IsWarnEnabled());
		//		Assert.True(tracer.IsErrorEnabled());
		//		Assert.True(tracer.IsSevereEnabled());
		//	}
		//}

		[Fact]
		public void RootLogWriterCanBeSetOnInitialization()
		{
			// Trace output is written to this guy
			var listLogWriter = new ListLogWriter<TraceEntry>();
			using (var traceManager = new TraceManager(listLogWriter))
			{
				traceManager.Start();
				var tracer = traceManager.TracerFor(this);
				tracer.Info("Info");

				Assert.Single(listLogWriter);
				TraceEntry traceEntry = listLogWriter.First();
				Assert.Equal(GetType().GetCSharpName(), traceEntry.TracerName);
				Assert.Equal(TraceLevel.Info, traceEntry.TraceLevel);
			}
		}

		[Fact]
		public void RootThresholdCanBeModifiedAfterLogging()
		{
			var listLogWriter = new ListLogWriter<TraceEntry>();
			// Start with threshold == Info
			var traceSwitch = new ThresholdTraceSwitch(TraceLevel.Info);
			using (var traceManager = new TraceManager(listLogWriter, traceSwitch))
			{
				traceManager.Start();
				var tracer = traceManager.TracerFor(this);

				// Log stuff
				tracer.Info("Info"); // Should log
				tracer.Verbose("Verbose"); // Shouldn't log
				Assert.Single(listLogWriter);

				// Change threshold
				traceSwitch.Threshold = TraceLevel.Verbose;

				// Log
				tracer.Info("Info");
				tracer.Verbose("Verbose"); // Should log
				tracer.Debug("Debug"); // Shouldn't log
				Assert.Equal(3, listLogWriter.Count());
			}
		}

		//[Fact]
		//public void RootLogWriterCanBeReplacedAfterLogging()
		//{
		//	// First log entry is written here
		//	var initialList = new ListLogWriter<TraceEntry>();
		//	var secondList = new ListLogWriter<TraceEntry>();

		//	var rootTracerConfig = new TracerConfig(Tracer.RootTracerName, new ThresholdTraceSwitch(TraceLevel.Info), initialList);
		//	using (var traceManager = new TraceManager(rootTracerConfig))
		//	{
		//		var tracer = traceManager.TracerFor(this);

		//		tracer.Info("Info");
		//		tracer.Verbose("Verbose"); // Shouldn't log
		//		Assert.Equal(1, initialList.Count);

		//		// Change LogWriter
		//		rootTracerConfig.Replace(null, secondList);

		//		// Log
		//		tracer.Info("Info");
		//		tracer.Verbose("Verbose"); // Shouldn't log
		//		Assert.Equal(1, secondList.Count);
		//	}			
		//}

		//[Fact]
		//public void RootLogWriterCanBeAddedAfterLogging()
		//{
		//	var initialList = new ListLogWriter<TraceEntry>();
		//	var secondList = new ListLogWriter<TraceEntry>();

		//	var rootTracerConfig = new TracerConfig(Tracer.RootTracerName, new ThresholdTraceSwitch(TraceLevel.Info), initialList);
		//	using (var traceManager = new TraceManager(rootTracerConfig))
		//	{
		//		var tracer = traceManager.TracerFor(this);

		//		tracer.Info("Info");
		//		tracer.Verbose("Verbose"); // Shouldn't log
		//		Assert.Equal(1, initialList.Count);

		//		// Add a LogWriter
		//		rootTracerConfig.Add(new OnOffTraceSwitch(true), secondList);

		//		// Log
		//		tracer.Info("Info");
		//		tracer.Verbose("Verbose"); // Shouldn't log to first
		//		Assert.Equal(2, initialList.Count);
		//		Assert.Equal(2, secondList.Count);
		//		Assert.DoesNotContain("Verbose", initialList.Select(entry => entry.Message));
		//		Assert.Contains("Verbose", secondList.Select(entry => entry.Message));
		//	}			
		//}

		[Fact(Skip = "Not yet implemented")]
		public void RootLogWriterCanBeAddedThenRemoved()
		{ }

		[Fact(Skip = "Not yet implemented")]
		public void NonRootThresholdCanBeModified()
		{ }

		[Fact(Skip = "Not yet implemented")]
		public void NonRootLogWriterCanBeReplaced()
		{ }

		[Fact(Skip = "Not yet implemented")]
		public void NonRootLogWriterCanBeAdded()
		{ }

		[Fact(Skip = "Not yet implemented")]
		public void NonRootLogWriterCanBeAddedThenRemoved()
		{ }

		[Fact(Skip = "Not yet implemented")]
		public void CanReadTraceManagerConfigFromFile()
		{ }

		[Fact]
		public void MultipleTraceLogWritersForSameNamePrefixWithDifferentSwitchThresholds()
		{
			var allListLogWriter = new ListLogWriter<TraceEntry>();
			var errorListLogWriter = new ListLogWriter<TraceEntry>();

			var traceWriterConfigAll = new TraceWriterConfig(allListLogWriter)
			                         {
				                         Switches =
				                         {
					                         { "LogJam.UnitTests", new OnOffTraceSwitch(true) }
				                         }
			                         };
			var traceWriterConfigErrors = new TraceWriterConfig(errorListLogWriter)
			                         {
				                         Switches =
				                         {
					                         { "LogJam.UnitTests", new ThresholdTraceSwitch(TraceLevel.Error) }
				                         }
			                         };
			using (var traceManager = new TraceManager(new TraceManagerConfig(traceWriterConfigAll, traceWriterConfigErrors)))
			{
				var tracer = traceManager.TracerFor(this);
				var fooTracer = traceManager.GetTracer("foo");

				tracer.Info("Info");
				tracer.Verbose("Verbose");
				tracer.Error("Error");
				tracer.Severe("Severe");

				// fooTracer shouldn't log to either of these lists
				fooTracer.Severe("foo Severe");

				Assert.Equal(2, errorListLogWriter.Count);
				Assert.Equal(4, allListLogWriter.Count);
			}
		}

		[Fact]
		public void CustomTraceFormatting()
		{
			// Text output is written here
			StringWriter traceOutput = new StringWriter();

			// Can either use a FormatAction, or subclass LogFormatter<TEntry>.  Here we're using a FormatAction.
			// Note that subclassing LogFormatter<TEntry> provides a slightly more efficient code-path.
			FormatAction<TraceEntry> format = (traceEntry, textWriter) => textWriter.WriteLine(traceEntry.TraceLevel);

			using (var traceManager = new TraceManager(new UseExistingTextWriterConfig<TraceEntry>(traceOutput, format)))
			{
				var tracer = traceManager.TracerFor(this);
				tracer.Info("m");
				tracer.Error("m");
			}

			Assert.Equal("Info\r\nError\r\n", traceOutput.ToString());
		}

		public static IEnumerable<object[]> TestTraceManagerConfigs
		{
			get
			{
				// test TraceManagerConfig #1
				var config = new TraceManagerConfig(
					new TraceWriterConfig()
					{
						LogWriterConfig = new ListLogWriterConfig<TraceEntry>(),
						Switches =
						{
							{ Tracer.All, new ThresholdTraceSwitch(TraceLevel.Info) },
							{ "Microsoft.WebApi.", new ThresholdTraceSwitch(TraceLevel.Warn) }
						}
					},
					new TraceWriterConfig()
					{
						LogWriterConfig = new DebuggerLogWriterConfig<TraceEntry>(),
						Switches =
						{
							{ Tracer.All, new ThresholdTraceSwitch(TraceLevel.Info) },
						}
					});
				yield return new object[] { config };
			}
		}

		[Theory]
		[PropertyData("TestTraceManagerConfigs")]
		public void CanRoundTripTraceManagerConfigToJson(TraceManagerConfig traceManagerConfig)
		{
			JsonSerializerSettings jsonSettings = new JsonSerializerSettings();
			jsonSettings.ContractResolver = new JsonConfigContractResolver(jsonSettings.ContractResolver);
			string json = JsonConvert.SerializeObject(traceManagerConfig, Formatting.Indented, jsonSettings);

			Console.WriteLine(json);

			// TODO: Deserialize back to TraceManagerConfig, then validate that the config is equal.
		}

		[Theory (Skip = "Not yet implemented")]
		[PropertyData("TestTraceManagerConfigs")]
		public void CanRoundTripTraceManagerConfigToXml(TraceManagerConfig traceManagerConfig)
		{
			// Serialize to xml
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(TraceManagerConfig));
			var sw = new StringWriter();
			xmlSerializer.Serialize(sw, traceManagerConfig);

			string xml = sw.ToString();
			Console.WriteLine(xml);

			// Deserialize back to TraceManagerConfig
			TraceManagerConfig deserializedConfig = (TraceManagerConfig) xmlSerializer.Deserialize(new StringReader(xml));

			Assert.Equal(traceManagerConfig, deserializedConfig);
		}

	}

}
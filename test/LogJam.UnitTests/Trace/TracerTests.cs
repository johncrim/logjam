﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TracerTests.cs">
// Copyright (c) 2011-2015 https://github.com/logjam2.  
// </copyright>
// Licensed under the <a href="https://github.com/logjam2/logjam/blob/master/LICENSE.txt">Apache License, Version 2.0</a>;
// you may not use this file except in compliance with the License.
// --------------------------------------------------------------------------------------------------------------------


namespace LogJam.UnitTests.Trace
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading.Tasks;

    using LogJam.Config;
    using LogJam.Internal.UnitTests.Examples;
    using LogJam.Test.Shared.Writers;
    using LogJam.Trace;
    using LogJam.Trace.Config;
    using LogJam.Trace.Format;
    using LogJam.Trace.Switches;
    using LogJam.UnitTests.Examples;
    using LogJam.Writer;

    using Xunit;
    using Xunit.Abstractions;
    using Xunit.Sdk;

    using TraceLevel = LogJam.Trace.TraceLevel;


    /// <summary>
    /// Verifies that <see cref="Tracer" /> behaves as expected.
    /// </summary>
    public sealed class TracerTests
    {

        private readonly ITestOutputHelper _testOutputHelper;

        public TracerTests(ITestOutputHelper testOutputHelper)
        {
            Contract.Requires<ArgumentNullException>(testOutputHelper != null);

            _testOutputHelper = testOutputHelper;
        }

        /// <summary>
        /// Shows how to verify tracing for a class under test by using <see cref="ListLogWriter{TEntry}" />
        /// </summary>
        [Fact]
        public void TracingCanBeVerifiedUsingListLogWriter()
        {
            var setupTracerFactory = new SetupLog();
            var listWriter = new ListLogWriter<TraceEntry>(setupTracerFactory);

            // Default threshold: Info for everything
            Tracer tracer;
            using (var traceManager = new TraceManager(listWriter))
            {
                tracer = traceManager.TracerFor(this);
                tracer.Info("By default info is enabled");
                tracer.Debug("This message won't be logged");
            }
            Assert.Single(listWriter);
            listWriter.Clear();

            // Tracing after TraceManager is disposed does nothing
            tracer.Info("Not logged, b/c TraceManager has been disposed.");
            Assert.Empty(listWriter);

            // Trace everything for the test class, nothing for other types
            using (var traceManager = new TraceManager(listWriter, new OnOffTraceSwitch(true), GetType().FullName))
            {
                tracer = traceManager.TracerFor(this);
                tracer.Debug("Now debug is enabled for this class");
            }
            Assert.Single(listWriter);
        }

        [Fact]
        public void LogWriterExceptionsDontPropagate()
        {
            var setupTracerFactory = new SetupLog();
            var exceptionLogWriter = new ExceptionThrowingLogWriter<TraceEntry>(setupTracerFactory);
            var listLogWriter = new ListLogWriter<TraceEntry>(setupTracerFactory);
            var traceManagerConfig = new TraceManagerConfig(
                new TraceWriterConfig()
                {
                    LogWriterConfig = new UseExistingLogWriterConfig(exceptionLogWriter, disposeOnStop: true),
                    Switches =
                    {
                        { Tracer.All, new OnOffTraceSwitch(true) }
                    }
                },
                new TraceWriterConfig(listLogWriter)
                {
                    LogWriterConfig = new UseExistingLogWriterConfig(listLogWriter, disposeOnStop: true),
                    Switches =
                    {
                        { Tracer.All, new OnOffTraceSwitch(true) }
                    }
                });

            var traceManager = new TraceManager(traceManagerConfig, setupTracerFactory);
            using (traceManager)
            {
                traceManager.Start();

                var tracer = traceManager.TracerFor(this);
                tracer.Info("Info");
                tracer.Warn("Warn");

                Assert.Equal(2, listLogWriter.Count());
                Assert.Equal(2, exceptionLogWriter.CountExceptionsThrown);

                // First write exception is reported in SetupLog
                // TODO: Replace logging to SetupLog with TraceWriter reporting its current status
                Assert.Equal(1, traceManager.SetupLog.Count(traceEntry => traceEntry.TraceLevel >= TraceLevel.Error && traceEntry.Details != null));
            }

            Assert.Equal(3, exceptionLogWriter.CountExceptionsThrown);

            // Exceptions should be reported in the SetupLog
            Assert.Equal(2, traceManager.SetupLog.Count(traceEntry => traceEntry.TraceLevel >= TraceLevel.Error && traceEntry.Details != null));
        }

        /// <summary>
        /// Ensures that TracerNames for generic types are consistent and readable
        /// </summary>
        [Fact]
        public void TracerNamesForGenericTypes()
        {
            var tracerFactory = new SetupLog();

            // ListLogWriter<MessageEntry> handling (generic type parameter)
            var tracer1 = tracerFactory.GetTracer(typeof(ListLogWriter<MessageEntry>));
            _testOutputHelper.WriteLine(tracer1.Name);
            var tracer2 = tracerFactory.GetTracer(typeof(ListLogWriter<>));
            _testOutputHelper.WriteLine(tracer2.Name);
            var tracer3 = tracerFactory.TracerFor(new ListLogWriter<MessageEntry>(new SetupLog()));
            _testOutputHelper.WriteLine(tracer3.Name);
            var tracer4 = tracerFactory.TracerFor<ListLogWriter<MessageEntry>>();
            _testOutputHelper.WriteLine(tracer4.Name);
            var tracer5 = tracerFactory.GetTracer(typeof(ListLogWriter<MessageEntry>).GetGenericTypeDefinition());
            _testOutputHelper.WriteLine(tracer5.Name);

            // PrivateClass.TestLogWriter<MessageEntry> handling (inner class + generic type parameter)
            tracer1 = tracerFactory.GetTracer(typeof(PrivateClass.TestEntryWriter<MessageEntry>));
            _testOutputHelper.WriteLine(tracer1.Name);
            tracer2 = tracerFactory.GetTracer(typeof(PrivateClass.TestEntryWriter<>));
            _testOutputHelper.WriteLine(tracer2.Name);
            tracer3 = tracerFactory.TracerFor(new PrivateClass.TestEntryWriter<MessageEntry>());
            _testOutputHelper.WriteLine(tracer3.Name);
            tracer4 = tracerFactory.TracerFor<PrivateClass.TestEntryWriter<MessageEntry>>();
            _testOutputHelper.WriteLine(tracer4.Name);
            tracer5 = tracerFactory.GetTracer(typeof(PrivateClass.TestEntryWriter<MessageEntry>).GetGenericTypeDefinition());
            _testOutputHelper.WriteLine(tracer5.Name);
        }

        [Fact]
        public void EnableTracingForAllGenericTypesWithSameGenericTypeDefinition()
        {
            var setupTracerFactory = new SetupLog();
            var traceConfig = new TraceWriterConfig(new ListLogWriter<TraceEntry>(setupTracerFactory))
                              {
                                  Switches =
                                  {
                                      { typeof(PrivateClass.TestEntryWriter<>), new OnOffTraceSwitch(true) }
                                  }
                              };

            using (var traceManager = new TraceManager(traceConfig, setupTracerFactory))
            {
                var tracer = traceManager.TracerFor(this);
                Assert.False(tracer.IsInfoEnabled());

                tracer = traceManager.GetTracer(typeof(PrivateClass.TestEntryWriter<>));
                Assert.True(tracer.IsInfoEnabled());

                tracer = traceManager.TracerFor<PrivateClass.TestEntryWriter<MessageEntry>>();
                Assert.True(tracer.IsInfoEnabled());

                tracer = traceManager.TracerFor<PrivateClass.TestEntryWriter<TraceEntry>>();
                Assert.True(tracer.IsInfoEnabled());
            }
        }

        [Fact]
        public void EnableTracingForSpecificGenericType()
        {
            var setupTracerFactory = new SetupLog();
            var traceConfig = new TraceWriterConfig(new ListLogWriter<TraceEntry>(setupTracerFactory))
                              {
                                  Switches =
                                  {
                                      { typeof(PrivateClass.TestEntryWriter<MessageEntry>), new OnOffTraceSwitch(true) }
                                  }
                              };
            using (var traceManager = new TraceManager(traceConfig, setupTracerFactory))
            {
                var tracer = traceManager.TracerFor(this);
                Assert.False(tracer.IsInfoEnabled());

                tracer = traceManager.GetTracer(typeof(PrivateClass.TestEntryWriter<>));
                Assert.False(tracer.IsInfoEnabled());

                tracer = traceManager.TracerFor<PrivateClass.TestEntryWriter<MessageEntry>>();
                Assert.True(tracer.IsInfoEnabled());

                tracer = traceManager.TracerFor<PrivateClass.TestEntryWriter<TraceEntry>>();
                Assert.False(tracer.IsInfoEnabled());
            }
        }


        /// <summary>
        /// Used just to test <see cref="Tracer" /> naming in <see cref="TracerTests.TracerNamesForGenericTypes" />
        /// </summary>
// ReSharper disable once ClassNeverInstantiated.Local
        private class PrivateClass
        {

            internal class TestEntryWriter<TEntry> : IEntryWriter<TEntry>
                where TEntry : ILogEntry
            {

                public void Write(ref TEntry entry)
                {
                    throw new NotImplementedException();
                }

                public bool IsEnabled { get { return true; } }

            }

        }

    }

}

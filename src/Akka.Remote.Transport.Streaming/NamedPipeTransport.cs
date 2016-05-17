﻿using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport.Streaming
{
    //TODO Work in progress
    public class NamedPipeTransportSettings : StreamTransportSettings
    {
        public string PipeName { get; private set; }

        public NamedPipeTransportSettings(Config config)
            : base(config)
        {
            PipeName = config.GetString("pipe-name");
        }
    }

    //TODO Find a way to make it work without port
    // Otherwise it might be ok to set the actual pipe name to {PipeName}_{port}

    public class NamedPipeTransport : StreamTransport
    {
        public const string ProtocolName = "pipe";

        public new NamedPipeTransportSettings Settings { get;  }

        public override string SchemeIdentifier
        {
            get { return ProtocolName; }
        }

        public NamedPipeTransport(ActorSystem system, Config config)
            : base(system, new NamedPipeTransportSettings(config))
        {
            
        }

        public NamedPipeTransport(ActorSystem system, NamedPipeTransportSettings settings)
            : base(system, settings)
        {
            Settings = settings;
        }

        protected override Address Initialize()
        {
            return new Address(ProtocolName, System.Name, Settings.PipeName, 1);
        }

        protected override void StartAcceptingConnections(IAssociationEventListener listener)
        {
            Task.Run(() => ListenLoop(listener));
        }

        protected override void Cleanup()
        {
            //TODO
        }

        private async Task ListenLoop(IAssociationEventListener listener)
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                NamedPipeServerStream stream = new NamedPipeServerStream(Settings.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                //TODO Catch exceptions appropriately
                await Task.Factory.FromAsync(stream.BeginWaitForConnection, stream.EndWaitForConnection, null);

                string uniqueId = Guid.NewGuid().ToString("N");
                var remoteAddress = new Address(ProtocolName, System.Name, Settings.PipeName + "_" + uniqueId, 1);

                var association = CreateInboundAssociation(stream, remoteAddress);

                listener.Notify(new InboundAssociation(association));
            }
        }

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            var association = CreateOutboundAssociation(remoteAddress);

            return Task.FromResult(association);
        }

        public AssociationHandle CreateInboundAssociation(NamedPipeServerStream stream, Address remoteAddress)
        {
            //TODO Add unique id in InboundAddress?
            var association = new StreamAssociationHandle(Settings, stream, InboundAddress, remoteAddress, null);
            RegisterAssociation(association);

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return association;
        }

        public AssociationHandle CreateOutboundAssociation(Address remoteAddress)
        {
            NamedPipeClientStream stream = new NamedPipeClientStream(".", remoteAddress.Host, PipeDirection.InOut, PipeOptions.Asynchronous);

            // Should not block, the server listens for `MaxAllowedServerInstances`
            // Connect will throw if the server is not listening
            stream.Connect();

            string uniqueId = Guid.NewGuid().ToString("N");
            var localAddress = new Address(ProtocolName, System.Name, Settings.PipeName + "_" + uniqueId, 1);

            var association = new StreamAssociationHandle(Settings, stream, localAddress, remoteAddress.WithPort(1), null);
            RegisterAssociation(association);

            association.ReadHandlerSource.Task.ContinueWith(task => association.Initialize(task.Result),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return association;
        }
    }
}

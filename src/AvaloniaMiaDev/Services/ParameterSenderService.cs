﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaMiaDev.OSC;
using Microsoft.Extensions.Hosting;

namespace AvaloniaMiaDev.Services;

public class ParameterSenderService : BackgroundService
{
    // We probably don't need a queue since we use osc message bundles, but for now, we're keeping it as
    // we might want to allow a way for the user to specify bundle or single message sends in the future
    private static readonly Queue<OscMessage> SendQueue = new();

    private readonly OscSendService _sendService;

    public ParameterSenderService(OscSendService sendService)
    {
        _sendService = sendService;
    }

    public static void Enqueue(OscMessage message) => SendQueue.Enqueue(message);
    public static void Clear() => SendQueue.Clear();

    protected async override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cancellationToken);

                // await UnifiedTracking.UpdateData(cancellationToken);

                // Send all messages in OSCParams.SendQueue
                if (SendQueue.Count <= 0)
                {
                    continue;
                }

                await _sendService.Send(SendQueue.ToArray(), cancellationToken);

                SendQueue.Clear();
            }
            catch (Exception e)
            {

            }
        }
    }
}

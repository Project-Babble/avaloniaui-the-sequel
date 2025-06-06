﻿using System.ComponentModel;

namespace Baballonia.Contracts;

public interface IOscTarget : INotifyPropertyChanged
{
    public bool IsConnected
    {
        get;
        set;
    }
    
    public int InPort
    {
        get;
        set;
    }
    
    public int OutPort
    {
        get;
        set;
    }
    
    public string DestinationAddress
    {
        get;
        set;
    }
}
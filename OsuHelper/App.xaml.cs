﻿// ------------------------------------------------------------------ 
//  Solution: <OsuHelper>
//  Project: <OsuHelper>
//  File: <App.xaml.cs>
//  Created By: Alexey Golub
//  Date: 20/08/2016
// ------------------------------------------------------------------ 

using GalaSoft.MvvmLight.Threading;

namespace OsuHelper
{
    public partial class App
    {
        static App()
        {
            DispatcherHelper.Initialize();
        }
    }
}
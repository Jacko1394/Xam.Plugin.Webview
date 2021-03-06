﻿using System;
using System.Threading;
using Android.Webkit;
using Android.Net.Http;
using Android.Graphics;
using Xam.Plugin.WebView.Abstractions;
using Android.Runtime;
using Android.Content;
using Xamarin.Forms;
using System.Collections.Generic;

namespace Xam.Plugin.WebView.Droid
{
    public class FormsWebViewClient : WebViewClient
    {

        readonly WeakReference<FormsWebViewRenderer> Reference;

        public FormsWebViewClient(FormsWebViewRenderer renderer)
        {
            Reference = new WeakReference<FormsWebViewRenderer>(renderer);
        }

        public override void OnReceivedHttpError(Android.Webkit.WebView view, IWebResourceRequest request, WebResourceResponse errorResponse)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null || (view as WebViewEx).Disposed) return;

            if (!request.IsForMainFrame || request.Url.ToString() != renderer.Control.Url.ToString())
            {
                base.OnReceivedHttpError(view, request, errorResponse);
                return;
            }

            renderer.Element.HandleNavigationError(errorResponse.StatusCode);
            renderer.Element.HandleNavigationCompleted(request.Url.ToString());
            renderer.Element.Navigating = false;
        }

        public override void OnReceivedError(Android.Webkit.WebView view, IWebResourceRequest request, WebResourceError error)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null || (view as WebViewEx).Disposed) return;

            if (request?.Url == null || renderer?.Control?.Url == null || error?.ErrorCode == null || !request.IsForMainFrame || request.Url.ToString() != renderer.Control.Url.ToString())
            {
                base.OnReceivedError(view, request, error);
                return;
            }

            renderer.Element.HandleNavigationError((int)error.ErrorCode);
            renderer.Element.HandleNavigationCompleted(request.Url.ToString());
            renderer.Element.Navigating = false;
        }

        //For Android < 5.0
        [Obsolete]
        public override void OnReceivedError(Android.Webkit.WebView view, [GeneratedEnum] ClientError errorCode, string description, string failingUrl)
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop) return;

            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null || (view as WebViewEx).Disposed) return;

            renderer.Element.HandleNavigationError((int)errorCode);
            renderer.Element.HandleNavigationCompleted(failingUrl.ToString());
            renderer.Element.Navigating = false;
        }

        //For Android < 5.0
        [Obsolete]
        public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView view, string url)
        {
            if ((view as WebViewEx).Disposed)
                return true;

            CheckResponseValidity(view, url);

            view.LoadUrl(url, FormsWebView.GlobalRegisteredHeaders);

            return true;
        }

        // NOTE: pulled fix from this unmerged PR - https://github.com/SKLn-Rad/Xam.Plugin.Webview/pull/104
        public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView view, IWebResourceRequest request)
        {
            if ((view as WebViewEx).Disposed)
                return base.ShouldOverrideUrlLoading(view, request);

            if (!CheckResponseValidity(view, request.Url.ToString()))
                return true;

            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer) || renderer?.Element?.BaseUrl == null)
                return base.ShouldOverrideUrlLoading(view, request);

            if (!request.Url.ToString().ToLower().StartsWith(renderer.Element.BaseUrl.ToLower()) || FormsWebView.GlobalRegisteredHeaders.Count == 0)
                return base.ShouldOverrideUrlLoading(view, request);

            if (request.RequestHeaders != null)
            {
                // Is recursive request? (check for our custom headers)
                bool needCustomHeader = false;
                foreach (var header in FormsWebView.GlobalRegisteredHeaders)
                {
                    if (!request.RequestHeaders.ContainsKey(header.Key))
                    {
                        needCustomHeader = true;
                        break;
                    }
                }

                if (!needCustomHeader)
                    return false;
            }

            // Add Additional headers
            var headers = new Dictionary<string, string>();

            if (request.RequestHeaders != null)
            {
                foreach (var header in request.RequestHeaders)
                    headers.Add(header.Key, header.Value);
            }

            foreach (var header in FormsWebView.GlobalRegisteredHeaders)
            {
                if (!headers.ContainsKey(header.Key))
                    headers.Add(header.Key, header.Value);
            }

            view.LoadUrl(request.Url.ToString(), headers);

            return true;
            //return base.ShouldOverrideUrlLoading(view, request);
        }

        bool CheckResponseValidity(Android.Webkit.WebView view, string url)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer))
            {
                return true;
            }

            if (renderer.Element == null || (view as WebViewEx).Disposed)
            {
                return true;
            }

            var response = renderer.Element.HandleNavigationStartRequest(url);

            return HandleDecisionHandlerDelegateResponse(view, url, response);
        }

        private bool HandleDecisionHandlerDelegateResponse(Android.Webkit.WebView view, string url, Abstractions.Delegates.DecisionHandlerDelegate response)
        {
            if ((view as WebViewEx).Disposed)
                return true;

            if (!response.Cancel && !response.OffloadOntoDevice)
            {
                return true;
            }
            var continueLoading = true;
            var finishedManualResetEvent = new ManualResetEvent(false);
            void CancelOrOffloadOntoDevice()
            {
                if (response.OffloadOntoDevice && !AttemptToHandleCustomUrlScheme(view, url))
                {
                    try
                    {
                        Device.OpenUri(new Uri(url));
                    }
                    catch { }
                }

                view.StopLoading();

                finishedManualResetEvent.Set();
                continueLoading = false;
            }

            if (Device.IsInvokeRequired)
            {
                Device.BeginInvokeOnMainThread(CancelOrOffloadOntoDevice);
            }
            else
            {
                CancelOrOffloadOntoDevice();
            }

            finishedManualResetEvent.WaitOne();

            return continueLoading;
        }

        public override void OnPageStarted(Android.Webkit.WebView view, string url, Bitmap favicon)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null) return;

            renderer.Element.Navigating = true;
        }

        bool AttemptToHandleCustomUrlScheme(Android.Webkit.WebView view, string url)
        {
            if (url.StartsWith("mailto"))
            {
                Android.Net.MailTo emailData = Android.Net.MailTo.Parse(url);

                Intent email = new Intent(Intent.ActionSendto);

                email.SetData(Android.Net.Uri.Parse("mailto:"));
                email.PutExtra(Intent.ExtraEmail, new String[] { emailData.To });
                email.PutExtra(Intent.ExtraSubject, emailData.Subject);
                email.PutExtra(Intent.ExtraCc, emailData.Cc);
                email.PutExtra(Intent.ExtraText, emailData.Body);

                if (email.ResolveActivity(Forms.Context.PackageManager) != null)
                    Forms.Context.StartActivity(email);

                return true;
            }

            if (url.StartsWith("http"))
            {
                Intent webPage = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
                if (webPage.ResolveActivity(Forms.Context.PackageManager) != null)
                    Forms.Context.StartActivity(webPage);

                return true;
            }

            return false;
        }

        public override void OnReceivedSslError(Android.Webkit.WebView view, SslErrorHandler handler, SslError error)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null) return;

            if (FormsWebViewRenderer.IgnoreSSLGlobally)
            {
                handler.Proceed();
            }

            else
            {
                handler.Cancel();
                renderer.Element.Navigating = false;
            }
        }

        public async override void OnPageFinished(Android.Webkit.WebView view, string url)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            if (renderer.Element == null || (view as WebViewEx).Disposed) return;

            // Add Injection Function
            await renderer.OnJavascriptInjectionRequest(FormsWebView.InjectedFunction);

            if (renderer?.Element == null || (view as WebViewEx).Disposed) return;


            // Add Global Callbacks
            if (renderer.Element.EnableGlobalCallbacks)
                foreach (var callback in FormsWebView.GlobalRegisteredCallbacks)
                    await renderer.OnJavascriptInjectionRequest(FormsWebView.GenerateFunctionScript(callback.Key));

            // Add Local Callbacks
            foreach (var callback in renderer.Element.LocalRegisteredCallbacks)
                await renderer.OnJavascriptInjectionRequest(FormsWebView.GenerateFunctionScript(callback.Key));

            if (renderer?.Element == null || (view as WebViewEx).Disposed) return;

            renderer.Element.CanGoBack = view.CanGoBack();
            renderer.Element.CanGoForward = view.CanGoForward();
            renderer.Element.Navigating = false;

            renderer.Element.HandleNavigationCompleted(url);
            renderer.Element.HandleContentLoaded();
        }
    }
}
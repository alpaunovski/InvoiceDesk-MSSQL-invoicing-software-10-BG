using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoiceDesk.Models;
using InvoiceDesk.Services.Auth;

namespace InvoiceDesk.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool IsNotBusy => !IsBusy;

    public event EventHandler<LoginResponse>? LoginSucceeded;
    public event EventHandler<string>? LoginFailed;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var response = await _authService.LoginAsync(Email.Trim(), Password, DeviceName.Trim());
            if (response?.Success == true && !string.IsNullOrWhiteSpace(response.Token))
            {
                Password = string.Empty;
                LoginSucceeded?.Invoke(this, response);
            }
            else
            {
                ErrorMessage = response?.ErrorMessage ?? "Invalid email or password";
                LoginFailed?.Invoke(this, ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Login failed: " + ex.Message;
            LoginFailed?.Invoke(this, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
}

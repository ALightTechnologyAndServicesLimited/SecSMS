using Microsoft.Extensions.DependencyInjection;

namespace SecSMS.Maui
{
    public partial class App : Application
    {
        readonly IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new NavigationPage(_serviceProvider.GetRequiredService<MainPage>()));
        }
    }
}
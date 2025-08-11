// Program.cs
using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using TekstilScada.Core;
using TekstilScada.UI;
using TekstilScada.Properties; // Settings i�in eklendi

namespace TekstilScada
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // --- YEN�: D�L AYARLARI ---
            // Uygulaman�n varsay�lan dilini T�rk�e olarak ayarl�yoruz.
            // Bu, .resx dosyalar�ndan do�ru olan�n se�ilmesini sa�lar.
            // --- YEN�: KAYITLI D�L� Y�KLEME ---
            // Kullan�c�n�n son se�ti�i dili ayar dosyas�ndan oku.
            // --- YEN�: KAYITLI D�L� Y�KLEME ---
            // Kullan�c�n�n son se�ti�i dili ayar dosyas�ndan oku.
         //   string savedLanguage = Settings.Default.UserLanguage;
          //  if (!string.IsNullOrEmpty(savedLanguage))
          //  {
          //      // E�er bir dil kay�tl�ysa, LanguageManager arac�l���yla uygula.
          //      LanguageManager.SetLanguage(savedLanguage);
            //}
            // --- Y�KLEME SONU ---

            // --- D�L AYARLARI SONU ---

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var loginForm = new LoginForm())
            {
                if (loginForm.ShowDialog() == DialogResult.OK)
                {
                    Application.Run(new MainForm());
                    
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Sjm;
class UiStateTest {
    static T Field<T>(object instance,string name) { return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance); }
    static void Require(bool value) { if(!value) throw new Exception("Pairing UI assertion failed"); }
    [STAThread] static int Main() {
        Application.EnableVisualStyles();
        string path=Path.GetFullPath(".local/ui-test-pairing.dat");
        try {
            string saved="192.168.1.2\n0123456789abcdef01234567\n"+new string('a',64);
            File.WriteAllBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(saved),null,DataProtectionScope.CurrentUser));
            using(var form=new ControlForm(path)) {
                Require(!Field<TextBox>(form,"host").Enabled && !Field<TextBox>(form,"key").Enabled);
                Require(Field<Button>(form,"connect").Text=="取消配对");
                Require(Field<Button>(form,"pause").Text=="重新连接");
                var health=form.GetType().GetMethod("ApplyHealth",BindingFlags.Instance|BindingFlags.NonPublic);
                form.GetType().GetField("userPaused",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                health.Invoke(form,new object[]{"OK"});
                Require(Field<bool>(form,"ready") && !Field<bool>(form,"armed"));
                health.Invoke(form,new object[]{"ERR STOPPED"});
                Require(!Field<bool>(form,"ready") && !Field<bool>(form,"armed"));
                Require(Field<Label>(form,"status").Text.Contains("自动重试"));
                health.Invoke(form,new object[]{"OK"});
                Require(Field<bool>(form,"ready") && !Field<bool>(form,"armed"));
                Require(File.Exists(path));
                form.GetType().GetMethod("CancelPairing",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                Require(!File.Exists(path));
                Require(Field<TextBox>(form,"host").Enabled && Field<TextBox>(form,"key").Enabled);
                Require(Field<TextBox>(form,"host").Text=="" && Field<TextBox>(form,"key").Text=="");
                Require(Field<Button>(form,"connect").Text=="连接手机");
                Require(!Field<Button>(form,"pause").Enabled && !Field<Button>(form,"next").Enabled);
                form.Dispose();
                Require(Field<bool>(form,"closing"));
                form.GetType().GetMethod("QueueSwipe",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{"UP"});
                Require(!form.IsHandleCreated);
            }
            Console.WriteLine("PASS: saved pairing locks inputs, disconnected paired state offers reconnect, cancel clears isolated credentials and restores form");
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
}

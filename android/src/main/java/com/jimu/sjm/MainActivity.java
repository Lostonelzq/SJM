package com.jimu.sjm;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.*;
import android.provider.Settings;
import android.view.*;
import android.widget.*;
import java.net.*;
import java.util.*;

public class MainActivity extends Activity {
    private final int ink = Color.rgb(24, 47, 43), muted = Color.rgb(88, 111, 105), green = Color.rgb(18, 107, 91);
    private TextView state, permission, address, secret;
    private Button toggle;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable refresh = new Runnable() { public void run() { update(); handler.postDelayed(this, 1000); } };
    private int dp(int n) { return (int)(n * getResources().getDisplayMetrics().density); }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        getWindow().setStatusBarColor(Color.rgb(245, 248, 246));
        getWindow().setNavigationBarColor(Color.rgb(245, 248, 246));
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true); scroll.setBackgroundColor(Color.rgb(245,248,246));
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setPadding(dp(24),dp(24),dp(24),dp(24));
        scroll.addView(root); setContentView(scroll);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            }
            return insets;
        });
        text(root, "轻划", 34, ink, true);
        text(root, "手放桌上，视频接着看。", 17, muted, false);
        LinearLayout summary = card(root);
        text(summary,"手机接收端",14,green,true);
        state = text(summary,"尚未开始接收",21,ink,true);
        text(summary,"只在你开启接收时响应电脑。",14,muted,false);
        LinearLayout step1 = card(root);
        text(step1,"01   允许上下滑动",19,ink,true);
        permission = text(step1,"无障碍服务尚未开启",15,muted,false);
        button(step1,"设置无障碍权限", () -> new AlertDialog.Builder(this).setTitle("允许轻划执行滑动")
            .setMessage("轻划通过无障碍执行已配对电脑发来的滑动、鼠标点击。输入测试需要访问当前焦点输入框并填字，不回传页面文字、不截图、不自动发送评论。\n\n系统会展示权限警告，请阅读后自行决定是否开启。")
            .setNegativeButton("取消",null).setPositiveButton("前往设置",(d,w)->startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))).show());
        LinearLayout step2 = card(root);
        text(step2,"02   连接你的电脑",19,ink,true);
        text(step2,"在 Windows 轻划中填写手机地址与 6 位配对码。配对一次，之后自动记住。",15,muted,false);
        address = text(step2,"",20,ink,true); address.setTextIsSelectable(true);
        text(step2,"配对码 · 2 分钟内有效",13,muted,false);
        secret = text(step2,"",19,green,true); secret.setTypeface(Typeface.MONOSPACE); secret.setTextIsSelectable(true);
        button(step2,"生成配对码并开启接收",()-> { Pairing.open(); startReceiver(); update(); });
        button(step2,"撤销已配对的电脑",()-> new AlertDialog.Builder(this).setTitle("撤销已有配对？").setMessage("旧电脑将无法继续控制，需重新输入 6 位配对码。")
            .setNegativeButton("取消",null).setPositiveButton("撤销",(d,w)-> { stopService(new Intent(this,ReceiverService.class)); ReceiverService.status="配对已撤销"; getSharedPreferences("pairing",MODE_PRIVATE).edit().putString("key",Protocol.randomHex(12)).putBoolean("paired",false).apply(); Pairing.open(); update(); }).show());
        toggle = button(root,"开始接收", () -> {
            if (ReceiverService.running) { stopService(new Intent(this,ReceiverService.class)); ReceiverService.status="已停止接收"; }
            else {
                if (!getSharedPreferences("pairing",MODE_PRIVATE).getBoolean("paired",false)) Pairing.open();
                startReceiver();
            }
            update();
        });
        text(root,"开启后切到视频 App，手机保持解锁。\n快捷键和鼠标滚轮方向以电脑端显示为准。",15,muted,false);
        button(root,"后台运行与小米设置", () -> new AlertDialog.Builder(this).setTitle("让连接保持稳定")
            .setMessage("若切到视频后失联：在系统应用信息中，允许轻划后台运行，并将电池策略调整为不限制（名称以系统为准）。\n\n如无障碍页面提示受限设置，可在应用信息右上角菜单查看是否提供「允许受限设置」，阅读系统提示后自行授权。\n\n手机重启或应用被强行停止后，请重新打开轻划并开始接收。")
            .setNegativeButton("关闭",null).setPositiveButton("应用信息",(d,w)->startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,android.net.Uri.parse("package:"+getPackageName())))).show());
        button(root,"设置与关于", () -> new AlertDialog.Builder(this).setTitle("轻划 1.0.8")
            .setItems(new String[]{"软件更新","隐私与使用说明","关于我们"},(d,w)->{
                String title = w==0?"软件更新":w==1?"隐私与使用说明":"关于我们";
                String message = w==0?"当前版本 1.0.8\n个人局域网试用版，暂未接入在线更新。后续通过新的安装包覆盖安装，签名保持一致。":w==1?"用途：由你本人操作电脑，对自己的手机执行上下滑动。\n\n数据：仅在本机保存配对密钥，电脑地址与密钥只用于局域网指令认证。仅为输入框测试访问当前焦点控件，不回传页面文字；不截图、不采集账号，不含广告或分析 SDK。指令支持滑动、鼠标点击和当前输入框填字；不会自动发送评论。\n\n停止：通知栏或首页可停止接收；更换密钥可撤销旧电脑。卸载清除手机端数据。电脑端可点击忘记配对。\n\n请只向信任的电脑提供密钥，勿在锁屏或涉及敏感操作时使用。":"北京极幕智娱科技有限公司\nkefu@jimuzhiyu.com\n\n轻划 · 个人试用版 1.0.8";
                new AlertDialog.Builder(this).setTitle(title).setMessage(message).setPositiveButton("关闭",null).show();
            }).show());
        update();
    }
    private void startReceiver() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        startForegroundService(new Intent(this,ReceiverService.class));
    }
    private void update() {
        if (state == null) return;
        state.setText(ReceiverService.running ? ReceiverService.displayStatus() : ReceiverService.status.startsWith("无法") ? ReceiverService.status : "接收已关闭");
        permission.setText(SwipeService.instance == null ? "未开启 · 需要你手动授权" : "已开启 · 支持滑动、指针点击与输入框测试");
        toggle.setText(ReceiverService.running?"停止接收":"开始接收");
        ArrayList<String> ips = new ArrayList<>();
        try { for (NetworkInterface nic : Collections.list(NetworkInterface.getNetworkInterfaces())) {
            if (!nic.isUp() || nic.isLoopback()) continue;
            for (InetAddress ip : Collections.list(nic.getInetAddresses())) if (ip instanceof Inet4Address && ip.isSiteLocalAddress()) ips.add(ip.getHostAddress());
        }} catch (Exception ignored) { }
        address.setText(ips.isEmpty()?"请连接 Wi-Fi":"地址："+String.join(" / ",ips));
        ReceiverService.key(this);
        String code = Pairing.visibleCode();
        secret.setText(code == null ? (getSharedPreferences("pairing",MODE_PRIVATE).getBoolean("paired",false) ? "已配对 · 电脑会记住连接" : "请点击生成新的配对码") : code.substring(0,3)+" "+code.substring(3)+"   · "+Pairing.secondsLeft()+" 秒");
    }
    private LinearLayout card(LinearLayout root) {
        LinearLayout card = new LinearLayout(this); card.setOrientation(LinearLayout.VERTICAL); card.setPadding(dp(18),dp(16),dp(18),dp(16));
        GradientDrawable bg = new GradientDrawable(); bg.setColor(Color.WHITE); bg.setCornerRadius(dp(20)); card.setBackground(bg);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1,-2); p.topMargin=dp(18); root.addView(card,p); return card;
    }
    private TextView text(LinearLayout root,String value,int size,int color,boolean bold) {
        TextView v = new TextView(this); v.setText(value); v.setTextSize(size); v.setTextColor(color); v.setPadding(0,dp(5),0,dp(5)); v.setLineSpacing(dp(3),1);
        if(bold) v.setTypeface(Typeface.DEFAULT,Typeface.BOLD); root.addView(v,new LinearLayout.LayoutParams(-1,-2)); return v;
    }
    private Button button(LinearLayout root,String value,Runnable action) {
        Button b = new Button(this); b.setText(value); b.setTextSize(16); b.setAllCaps(false); b.setTextColor(Color.WHITE); b.setMinHeight(dp(52));
        b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(green));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1,-2); p.topMargin=dp(10); root.addView(b,p); b.setOnClickListener(v->action.run()); return b;
    }
    @Override protected void onResume() { super.onResume(); handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}

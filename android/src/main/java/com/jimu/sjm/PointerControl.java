package com.jimu.sjm;

import android.accessibilityservice.*;
import android.app.KeyguardManager;
import android.graphics.*;
import android.os.*;
import android.view.*;
import android.view.accessibility.AccessibilityNodeInfo;
import java.util.concurrent.*;

/** Coordinates only; no screenshots or page text are returned to the computer. */
final class PointerControl {
    private final SwipeService service;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final WindowManager wm;
    private View cursor;
    private final Object movementLock=new Object();
    private int pendingX,pendingY,epoch;
    private boolean framePending;
    private long received,drawn;
    private long lastInput;
    private final Runnable expiry = this::hide;
    PointerControl(SwipeService service) { this.service=service; wm=service.getSystemService(WindowManager.class); }
    void hide() {
        handler.removeCallbacks(expiry);
        if(cursor!=null) { try { wm.removeView(cursor); } catch(RuntimeException ignored) { } cursor=null; }
    }
    void stop() {
        synchronized(movementLock) { epoch++;framePending=false; }
        handler.post(()-> { hide();android.util.Log.i("SJM","Pointer frames received="+received+", drawn="+drawn);received=drawn=0; });
    }
    void keepAlive() {
        handler.post(()-> { if(!allowed()) { hide();return; } if(cursor!=null) { handler.removeCallbacks(expiry);handler.postDelayed(expiry,800); } });
    }
    String move(String command) {
        String[] parts=command.split(":");
        int x=Integer.parseInt(parts[1]),y=Integer.parseInt(parts[2]);
        if(x<0 || x>10000 || y<0 || y>10000) return "ERR COMMAND";
        if(!allowed()) return "ERR LOCKED";
        final int current;
        synchronized(movementLock) {
            pendingX=x;pendingY=y;received++;lastInput=SystemClock.elapsedRealtime();
            if(framePending) return "OK";
            framePending=true;current=epoch;
        }
        handler.post(()-> {
            synchronized(movementLock) { if(current!=epoch) return; }
            Choreographer.getInstance().postFrameCallback(time-> {
                int nx,ny;long age;
                synchronized(movementLock) {
                    if(current!=epoch) return;
                    framePending=false;nx=pendingX;ny=pendingY;age=SystemClock.elapsedRealtime()-lastInput;
                }
                if(age>500 || !allowed()) { hide();return; }
                try {
                    android.util.DisplayMetrics metrics=new android.util.DisplayMetrics();wm.getDefaultDisplay().getRealMetrics(metrics);
                    showPointer(nx*(metrics.widthPixels-1)/10000f,ny*(metrics.heightPixels-1)/10000f);drawn++;
                } catch(RuntimeException e) { hide(); }
            });
        });
        return "OK";
    }
    private boolean allowed() {
        return ReceiverService.running && service.getSystemService(PowerManager.class).isInteractive()
            && !service.getSystemService(KeyguardManager.class).isKeyguardLocked();
    }
    String execute(String command) {
        CountDownLatch done=new CountDownLatch(1);
        String[] result={"ERR TIMEOUT"};
        long deadline=SystemClock.elapsedRealtime()+1400;
        handler.post(()-> {
            try {
                if(SystemClock.elapsedRealtime()>deadline) { result[0]="ERR EXPIRED"; return; }
                if(!allowed()) { hide(); result[0]="ERR LOCKED"; return; }
                if(command.equals("FOCUS") || command.startsWith("TEXT:")) { result[0]=text(command); return; }
                if(command.equals("HIDE")) { hide(); result[0]="OK"; return; }
                if(!command.matches("(MOVE|TAP):[0-9]{1,5}:[0-9]{1,5}")) { result[0]="ERR COMMAND"; return; }
                String[] parts=command.split(":"); int nx=Integer.parseInt(parts[1]),ny=Integer.parseInt(parts[2]);
                if(nx>10000 || ny>10000) { result[0]="ERR COMMAND"; return; }
                android.util.DisplayMetrics metrics=new android.util.DisplayMetrics(); wm.getDefaultDisplay().getRealMetrics(metrics);
                float x=nx*(metrics.widthPixels-1)/10000f,y=ny*(metrics.heightPixels-1)/10000f;
                if(parts[0].equals("MOVE")) {
                    showPointer(x,y); result[0]="OK";
                } else {
                    Path path=new Path(); path.moveTo(x,y);
                    GestureDescription gesture=new GestureDescription.Builder().addStroke(new GestureDescription.StrokeDescription(path,0,60)).build();
                    boolean accepted=service.dispatchGesture(gesture,new AccessibilityService.GestureResultCallback() {
                        @Override public void onCompleted(GestureDescription g) { result[0]="OK";done.countDown(); }
                        @Override public void onCancelled(GestureDescription g) { result[0]="ERR CANCELLED";done.countDown(); }
                    },handler);
                    if(accepted) return; result[0]="ERR GESTURE";
                }
            } catch(RuntimeException e) { result[0]="ERR POINTER"; }
            finally { if(!command.startsWith("TAP:") || !result[0].equals("ERR TIMEOUT")) done.countDown(); }
        });
        try { done.await(1500,TimeUnit.MILLISECONDS); } catch(InterruptedException e) { Thread.currentThread().interrupt(); }
        return result[0];
    }
    private void showPointer(float x,float y) {
                    float density=service.getResources().getDisplayMetrics().density;
                    int tip=Math.round(2*density);
                    if(cursor==null) {
                        cursor=new View(service) {
                            final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
                            @Override protected void onDraw(Canvas canvas) {
                                canvas.save();canvas.scale(density,density);
                                Path arrow=new Path();arrow.moveTo(2,2);arrow.lineTo(2,26);arrow.lineTo(8.5f,20);
                                arrow.lineTo(14,31);arrow.lineTo(18,29);arrow.lineTo(12,18);arrow.lineTo(22,18);arrow.close();
                                paint.setColor(Color.WHITE);paint.setStyle(Paint.Style.FILL);canvas.drawPath(arrow,paint);
                                paint.setColor(Color.BLACK);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(1.5f);paint.setStrokeJoin(Paint.Join.ROUND);canvas.drawPath(arrow,paint);
                                canvas.restore();
                            }
                        };
                        WindowManager.LayoutParams p=new WindowManager.LayoutParams(Math.round(25*density),Math.round(34*density),WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE|WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,PixelFormat.TRANSLUCENT);
                        p.gravity=Gravity.TOP|Gravity.LEFT; if(Build.VERSION.SDK_INT>=28) p.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
                        p.x=Math.round(x)-tip; p.y=Math.round(y)-tip; wm.addView(cursor,p);
                    } else {
                        WindowManager.LayoutParams p=(WindowManager.LayoutParams)cursor.getLayoutParams(); p.x=Math.round(x)-tip;p.y=Math.round(y)-tip;wm.updateViewLayout(cursor,p);
                    }
                    handler.removeCallbacks(expiry);handler.postDelayed(expiry,800);
    }
    private String text(String command) {
        AccessibilityNodeInfo node=service.findFocus(AccessibilityNodeInfo.FOCUS_INPUT);
        if(node==null) return "ERR NO_FOCUS";
        try {
            if(node.isPassword()) return "ERR PASSWORD";
            android.util.Log.i("SJM","Input capability: class="+node.getClassName()+", editable="+node.isEditable()+", setText="+node.getActionList().contains(AccessibilityNodeInfo.AccessibilityAction.ACTION_SET_TEXT));
            if(!node.isEditable() && !String.valueOf(node.getClassName()).contains("EditText")) return "ERR NO_SET_TEXT";
            if(command.equals("FOCUS")) return "OK SET_TEXT";
            String value=new String(android.util.Base64.decode(command.substring(5),android.util.Base64.NO_WRAP),java.nio.charset.StandardCharsets.UTF_8);
            Bundle args=new Bundle();args.putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,value);
            return node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT,args)?"OK":"ERR SET_TEXT";
        } finally { node.recycle(); }
    }
}

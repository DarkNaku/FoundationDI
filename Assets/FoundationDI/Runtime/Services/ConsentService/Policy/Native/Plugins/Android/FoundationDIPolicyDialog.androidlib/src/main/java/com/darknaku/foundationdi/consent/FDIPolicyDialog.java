package com.darknaku.foundationdi.consent;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.DialogInterface;
import android.text.SpannableString;
import android.text.Spanned;
import android.text.method.LinkMovementMethod;
import android.text.style.URLSpan;
import android.util.Log;
import android.view.View;
import android.widget.TextView;

// FoundationDI 네이티브 약관 창(Android). 본문 속 링크가 눌리는 AlertDialog다.
// C#(AndroidNativePolicyDialog)이 어느 스레드에서 불러도 UI 스레드로 넘겨 처리한다.
public final class FDIPolicyDialog
{
    private static final String TAG = "FoundationDI";

    public interface Listener
    {
        void onAccepted();
    }

    private static AlertDialog current;

    private FDIPolicyDialog()
    {
    }

    // starts/lengths는 UTF-16 단위다(C# string과 Java String이 같다).
    public static void show(final Activity activity, final String title, final String message, final int[] starts,
                            final int[] lengths, final String[] urls, final String acceptLabel,
                            final Listener listener)
    {
        activity.runOnUiThread(new Runnable()
        {
            @Override
            public void run()
            {
                dismissCurrent();

                SpannableString text = new SpannableString(message);

                for (int i = 0; i < urls.length; i++)
                {
                    int end = starts[i] + lengths[i];

                    if (starts[i] < 0 || end > text.length()) continue;

                    text.setSpan(new SafeUrlSpan(urls[i]), starts[i], end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                }

                AlertDialog.Builder builder = new AlertDialog.Builder(activity)
                    .setMessage(text)
                    .setCancelable(false)
                    .setPositiveButton(acceptLabel, new DialogInterface.OnClickListener()
                    {
                        @Override
                        public void onClick(DialogInterface dialog, int which)
                        {
                            current = null;
                            listener.onAccepted();
                        }
                    });

                if (title != null && !title.isEmpty()) builder.setTitle(title);

                AlertDialog dialog = builder.create();
                dialog.setCanceledOnTouchOutside(false);
                dialog.show();

                // 메시지 TextView는 show() 뒤에야 생긴다. 이걸 걸어야 링크가 눌린다.
                TextView body = (TextView) dialog.findViewById(android.R.id.message);
                if (body != null) body.setMovementMethod(LinkMovementMethod.getInstance());

                current = dialog;
            }
        });
    }

    public static void close(final Activity activity)
    {
        activity.runOnUiThread(new Runnable()
        {
            @Override
            public void run()
            {
                dismissCurrent();
            }
        });
    }

    private static void dismissCurrent()
    {
        if (current == null) return;

        current.dismiss();
        current = null;
    }

    // 브라우저가 없는 기기에서 링크를 누르면 URLSpan이 ActivityNotFoundException으로 앱을 죽인다.
    private static final class SafeUrlSpan extends URLSpan
    {
        SafeUrlSpan(String url)
        {
            super(url);
        }

        @Override
        public void onClick(View widget)
        {
            try
            {
                super.onClick(widget);
            }
            catch (ActivityNotFoundException e)
            {
                Log.w(TAG, "약관 링크를 열 앱이 없다: " + getURL());
            }
        }
    }
}

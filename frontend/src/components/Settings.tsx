import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { useUserSettings } from '../hooks/useUserSettings';

type Props = {
  open: boolean;
  onClose: () => void;
};

export default function Settings({ open, onClose }: Props) {
  const { settings, setSettings } = useUserSettings();
  const [enableNotifications, setEnableNotifications] = useState(settings.enableNotifications);
  const [threshold, setThreshold] = useState(settings.signalStrengthThreshold);

  useEffect(() => {
    setEnableNotifications(settings.enableNotifications);
    setThreshold(settings.signalStrengthThreshold);
  }, [settings.enableNotifications, settings.signalStrengthThreshold]);

  const onSave = async () => {
    setSettings({
      enableNotifications,
      signalStrengthThreshold: Math.min(100, Math.max(0, threshold)),
    });

    if (enableNotifications && typeof window !== 'undefined' && 'Notification' in window) {
      if (Notification.permission === 'default') {
        const result = await Notification.requestPermission();
        if (result !== 'granted') {
          toast.error('Notifications are blocked by the browser');
        }
      }
    }

    toast.success('Settings saved');
    onClose();
  };

  if (!open) return null;

  return (
    <div className="modal modal-open">
      <div className="modal-box">
        <h3 className="font-bold text-lg">Settings</h3>

        <div className="mt-4 space-y-4">
          <div className="form-control">
            <label className="label cursor-pointer">
              <span className="label-text">Enable browser notifications</span>
              <input
                type="checkbox"
                className="toggle"
                checked={enableNotifications}
                onChange={(e) => setEnableNotifications(e.target.checked)}
              />
            </label>
          </div>

          <label className="form-control">
            <div className="label">
              <span className="label-text">Signal strength threshold</span>
              <span className="label-text-alt opacity-70">{threshold}</span>
            </div>
            <input
              type="range"
              min={0}
              max={100}
              className="range range-primary"
              value={threshold}
              onChange={(e) => setThreshold(Number(e.target.value))}
            />
          </label>
        </div>

        <div className="modal-action">
          <button className="btn btn-ghost" type="button" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" type="button" onClick={onSave}>
            Save
          </button>
        </div>
      </div>
      <button className="modal-backdrop" type="button" onClick={onClose} aria-label="Close settings" />
    </div>
  );
}


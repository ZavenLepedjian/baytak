import React, { useCallback, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Image, Modal, Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import type { ImageStyle, StyleProp } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as ImagePicker from 'expo-image-picker';
import { manipulateAsync, SaveFormat } from 'expo-image-manipulator';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { buildUrl } from '../api/url';
import { useApi } from '../api/useApi';
import { useAuth } from '../auth/AuthProvider';
import { API_BASE_URL, API_KEY } from '../config';
import { colors, HIT, radius, spacing } from '../theme';
import { Button, Text, showMessage } from './ui';

export const MAX_RECEIPT_PHOTOS = 5;
/** The longest edge of an uploaded photo. A bill stays readable and the file stays around 300 KB. */
const MAX_EDGE = 1600;
const JPEG_QUALITY = 0.7;

export type PickedPhoto = { key: number; uri: string; status: 'uploading' | 'ready' | 'failed'; photoId?: string };

/** Shrinks and re-encodes as JPEG. Re-encoding also drops the photo's metadata, including where it was taken. */
async function shrink(asset: ImagePicker.ImagePickerAsset): Promise<string> {
  const longest = Math.max(asset.width, asset.height);
  const actions = longest > MAX_EDGE ? [{ resize: asset.width >= asset.height ? { width: MAX_EDGE } : { height: MAX_EDGE } }] : [];
  const result = await manipulateAsync(asset.uri, actions, { compress: JPEG_QUALITY, format: SaveFormat.JPEG });
  return result.uri;
}

/** Picks photos (camera or library), shrinks them and uploads each one as soon as it is chosen. */
export function usePhotoUploads(max = MAX_RECEIPT_PHOTOS) {
  const api = useApi();
  const [photos, setPhotos] = useState<PickedPhoto[]>([]);
  const listRef = useRef<PickedPhoto[]>([]);
  listRef.current = photos;
  const nextKey = useRef(1);

  const patch = useCallback((key: number, change: Partial<PickedPhoto>) => {
    setPhotos((list) => list.map((p) => (p.key === key ? { ...p, ...change } : p)));
  }, []);

  const upload = useCallback(
    async (key: number, uri: string) => {
      try {
        const blob = await (await fetch(uri)).blob();
        const res = await api.upload<{ photoId: string }>('receipt/photo', blob, 'image/jpeg');
        if (res.ok && res.data?.photoId) patch(key, { status: 'ready', photoId: res.data.photoId });
        else patch(key, { status: 'failed' });
      } catch {
        patch(key, { status: 'failed' });
      }
    },
    [api, patch],
  );

  const addAssets = useCallback(
    async (assets: ImagePicker.ImagePickerAsset[]) => {
      const room = max - listRef.current.length;
      const chosen = assets.slice(0, Math.max(0, room));
      const added = chosen.map((asset) => ({ asset, key: nextKey.current++ }));
      setPhotos((list) => [...list, ...added.map(({ asset, key }): PickedPhoto => ({ key, uri: asset.uri, status: 'uploading' }))]);
      for (const { asset, key } of added) {
        try {
          const uri = await shrink(asset);
          patch(key, { uri });
          await upload(key, uri);
        } catch {
          patch(key, { status: 'failed' });
        }
      }
    },
    [max, patch, upload],
  );

  const takePhoto = useCallback(async () => {
    if (listRef.current.length >= max) return;
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      showMessage({ message: 'Allow camera access in your phone settings to take a photo', type: 'warning' });
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 1, exif: false });
    if (!result.canceled) await addAssets(result.assets);
  }, [addAssets, max]);

  const choosePhotos = useCallback(async () => {
    const room = max - listRef.current.length;
    if (room <= 0) return;
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      allowsMultipleSelection: true,
      selectionLimit: room,
      quality: 1,
      exif: false,
    });
    if (!result.canceled) await addAssets(result.assets);
  }, [addAssets, max]);

  const retry = useCallback(
    (key: number) => {
      const photo = listRef.current.find((p) => p.key === key);
      if (!photo) return;
      patch(key, { status: 'uploading' });
      void upload(key, photo.uri);
    },
    [patch, upload],
  );

  const remove = useCallback((key: number) => setPhotos((list) => list.filter((p) => p.key !== key)), []);
  const clear = useCallback(() => setPhotos([]), []);

  return {
    photos,
    takePhoto,
    choosePhotos,
    retry,
    remove,
    clear,
    max,
    uploading: photos.some((p) => p.status === 'uploading'),
    failed: photos.some((p) => p.status === 'failed'),
    readyIds: photos.filter((p) => p.status === 'ready' && p.photoId).map((p) => p.photoId as string),
  };
}

export type PhotoUploads = ReturnType<typeof usePhotoUploads>;

/** The photos chosen for a receipt that is being created, with the buttons to add more. */
export function PhotoPicker({ uploads }: { uploads: PhotoUploads }) {
  const { photos, max } = uploads;
  const full = photos.length >= max;
  return (
    <View>
      {photos.length > 0 ? (
        <View style={styles.strip}>
          {photos.map((p, i) => (
            <View key={p.key} style={styles.thumbBox}>
              <Image source={{ uri: p.uri }} style={styles.thumb} accessibilityIgnoresInvertColors />
              {p.status === 'uploading' ? (
                <View style={styles.cover} accessible accessibilityLabel={`Photo ${i + 1}, uploading`}>
                  <ActivityIndicator color={colors.onInk} />
                </View>
              ) : null}
              {p.status === 'failed' ? (
                <Pressable
                  style={[styles.cover, styles.coverFailed]}
                  accessibilityRole="button"
                  accessibilityLabel={`Photo ${i + 1} did not upload. Try again`}
                  onPress={() => uploads.retry(p.key)}
                >
                  <Ionicons name="refresh" size={22} color={colors.onInk} />
                  <Text variant="label" color={colors.onInk}>
                    Retry
                  </Text>
                </Pressable>
              ) : null}
              <Pressable
                style={styles.remove}
                hitSlop={8}
                accessibilityRole="button"
                accessibilityLabel={`Remove photo ${i + 1}`}
                onPress={() => uploads.remove(p.key)}
              >
                <Ionicons name="close-circle" size={24} color={colors.text} />
              </Pressable>
            </View>
          ))}
        </View>
      ) : null}
      <View style={styles.buttons}>
        <Button small variant="secondary" icon="camera-outline" title="Take photo" disabled={full} onPress={uploads.takePhoto} style={{ flex: 1 }} />
        <Button small variant="secondary" icon="images-outline" title="Choose photos" disabled={full} onPress={uploads.choosePhotos} style={{ flex: 1 }} />
      </View>
      <Text variant="caption" color={colors.textMuted} style={{ marginTop: spacing.sm }}>
        {photos.length} of {max} photos. Residents who receive this receipt can see them.
      </Text>
    </View>
  );
}

/** An image that lives behind sign-in: the request carries the session token, like every other call. */
function AuthedImage({ id, style, contain }: { id: string; style: StyleProp<ImageStyle>; contain?: boolean }) {
  const { token } = useAuth();
  const [failed, setFailed] = useState(false);
  const source = useMemo(
    () => ({ uri: buildUrl(API_BASE_URL, `receipt/photo/${id}`, API_KEY), headers: { Authorization: `Bearer ${token ?? ''}` } }),
    [id, token],
  );
  if (failed) {
    return (
      <View style={[style as object, styles.missing]}>
        <Ionicons name="image-outline" size={26} color={colors.textFaint} />
      </View>
    );
  }
  return <Image source={source} style={style} resizeMode={contain ? 'contain' : 'cover'} onError={() => setFailed(true)} accessibilityIgnoresInvertColors />;
}

/** Small photos under a receipt; a tap opens them full screen. */
export function ReceiptPhotoStrip({ ids }: { ids: string[] }) {
  const [open, setOpen] = useState<number | null>(null);
  if (ids.length === 0) return null;
  return (
    <View style={{ marginTop: spacing.md }}>
      <Text variant="label" color={colors.textMuted}>
        PHOTOS OF THE BILL
      </Text>
      <View style={[styles.strip, { marginTop: spacing.sm }]}>
        {ids.map((id, i) => (
          <Pressable
            key={id}
            accessibilityRole="imagebutton"
            accessibilityLabel={`Photo ${i + 1} of ${ids.length}. Opens full screen`}
            onPress={() => setOpen(i)}
          >
            <AuthedImage id={id} style={styles.thumb} />
          </Pressable>
        ))}
      </View>
      <PhotoViewer ids={ids} start={open} onClose={() => setOpen(null)} />
    </View>
  );
}

function PhotoViewer({ ids, start, onClose }: { ids: string[]; start: number | null; onClose: () => void }) {
  const { width, height } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const [current, setCurrent] = useState(0);
  const visible = start !== null;
  return (
    <Modal visible={visible} animationType="fade" onRequestClose={onClose} onShow={() => setCurrent(start ?? 0)} statusBarTranslucent>
      <View style={styles.viewer}>
        {visible ? (
          <FlatList
            data={ids}
            keyExtractor={(id) => id}
            horizontal
            pagingEnabled
            initialScrollIndex={start ?? 0}
            getItemLayout={(_, index) => ({ length: width, offset: width * index, index })}
            showsHorizontalScrollIndicator={false}
            onMomentumScrollEnd={(e) => setCurrent(Math.round(e.nativeEvent.contentOffset.x / width))}
            renderItem={({ item }) => (
              <View style={{ width, height, justifyContent: 'center' }}>
                <AuthedImage id={item} style={{ width, height: height - insets.top - insets.bottom - 120 }} contain />
              </View>
            )}
          />
        ) : null}
        <View style={[styles.viewerBar, { paddingTop: insets.top + spacing.sm }]} pointerEvents="box-none">
          <Text variant="bodyStrong" color={colors.onInk} accessibilityLiveRegion="polite">
            {current + 1} of {ids.length}
          </Text>
          <Pressable style={styles.close} accessibilityRole="button" accessibilityLabel="Close photos" onPress={onClose} hitSlop={8}>
            <Ionicons name="close" size={28} color={colors.onInk} />
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}

const THUMB = 72;

const styles = StyleSheet.create({
  strip: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginBottom: spacing.md },
  thumbBox: { width: THUMB, height: THUMB },
  thumb: { width: THUMB, height: THUMB, borderRadius: radius.md, backgroundColor: colors.surfaceMuted },
  missing: { width: THUMB, height: THUMB, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.surfaceMuted, borderRadius: radius.md },
  cover: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    borderRadius: radius.md,
    backgroundColor: 'rgba(5, 48, 46, 0.55)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  coverFailed: { backgroundColor: 'rgba(143, 40, 21, 0.85)' },
  remove: { position: 'absolute', top: -8, right: -8, backgroundColor: colors.surface, borderRadius: 12 },
  buttons: { flexDirection: 'row', gap: spacing.sm },
  viewer: { flex: 1, backgroundColor: '#000' },
  viewerBar: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.sm,
    backgroundColor: 'rgba(0,0,0,0.45)',
  },
  close: { minWidth: HIT, minHeight: HIT, alignItems: 'center', justifyContent: 'center' },
});

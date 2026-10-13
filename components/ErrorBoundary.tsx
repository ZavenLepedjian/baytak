import React from 'react';
import { StyleSheet, View } from 'react-native';
import { Button, Text } from './ui';
import { colors, spacing } from '../theme';

type State = { error: Error | null };

/** Last line of defence: a render error shows a recoverable screen instead of a white crash. */
export class ErrorBoundary extends React.Component<{ children: React.ReactNode }, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    if (__DEV__) console.error('Unhandled UI error', error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <View style={styles.root}>
        <Text variant="title" align="center">
          Something went wrong
        </Text>
        <Text variant="body" color={colors.textMuted} align="center" style={{ marginVertical: spacing.md }}>
          The app hit an unexpected problem. You can try again.
        </Text>
        <Button title="Try again" onPress={() => this.setState({ error: null })} style={{ alignSelf: 'stretch' }} />
      </View>
    );
  }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: colors.bg, justifyContent: 'center', padding: spacing.xl },
});
